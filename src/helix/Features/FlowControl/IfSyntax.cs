﻿using Helix.Analysis.Types;
using Helix.Analysis;
using Helix.Features.FlowControl;
using Helix.Features.Primitives;
using Helix.Generation.Syntax;
using Helix.Generation;
using Helix.Parsing;
using System.Reflection;
using Helix.Features.Variables;

namespace Helix.Parsing {
    public partial class Parser {
        private ISyntaxTree IfExpression() {
            var start = this.Advance(TokenKind.IfKeyword);
            var cond = this.TopExpression();

            this.Advance(TokenKind.ThenKeyword);
            var affirm = this.TopExpression();

            if (this.TryAdvance(TokenKind.ElseKeyword)) {
                var neg = this.TopExpression();
                var loc = start.Location.Span(neg.Location);

                return new IfParseSyntax(loc, cond, affirm, neg);
            }
            else {
                var loc = start.Location.Span(affirm.Location);

                return new IfParseSyntax(loc, cond, affirm);
            }
        }
    }
}

namespace Helix.Features.FlowControl {
    public record IfParseSyntax : ISyntaxTree {
        private static int ifTempCounter = 0;

        private readonly ISyntaxTree cond, iftrue, iffalse;
        private readonly IdentifierPath tempPath;

        public TokenLocation Location { get; }

        public IEnumerable<ISyntaxTree> Children => new[] { this.cond, this.iftrue, this.iffalse };

        public bool IsPure { get; }

        public IfParseSyntax(TokenLocation location, ISyntaxTree cond, 
            ISyntaxTree iftrue) {

            this.Location = location;
            this.cond = cond;

            this.iftrue = new BlockSyntax(iftrue.Location, new ISyntaxTree[] {
                iftrue, new VoidLiteral(iftrue.Location)
            });

            this.iffalse = new VoidLiteral(location);
            this.IsPure = cond.IsPure && iftrue.IsPure;
            this.tempPath = new IdentifierPath("$if_temp_" + ifTempCounter++);
        }

        public IfParseSyntax(TokenLocation location, ISyntaxTree cond, ISyntaxTree iftrue, 
            ISyntaxTree iffalse) : this(location, cond, iftrue) {

            this.Location = location;
            this.cond = cond;
            this.iftrue = iftrue;
            this.iffalse = iffalse;
            this.IsPure = cond.IsPure && iftrue.IsPure && iffalse.IsPure;
        }

        public ISyntaxTree CheckTypes(SyntaxFrame types) {
            var iftrueTypes = new SyntaxFrame(types);
            var iffalseTypes = new SyntaxFrame(types);

            var cond = this.cond.CheckTypes(types).ToRValue(types).UnifyTo(PrimitiveType.Bool, types);
            var iftrue = this.iftrue.CheckTypes(iftrueTypes).ToRValue(iftrueTypes);
            var iffalse = this.iffalse.CheckTypes(iffalseTypes).ToRValue(iffalseTypes);

            iftrue = iftrue.UnifyFrom(iffalse, types);
            iffalse = iffalse.UnifyFrom(iftrue, types);

            var modifiedVars = iftrueTypes.Variables
                .Concat(iffalseTypes.Variables)
                .Select(x => x.Key)
                .Intersect(types.Variables.Select(x => x.Key));

            var newLifetimes = new List<VariableSignature>();

            // For every variable mutated within this if statement, we need to create a new
            // lifetime for that variable after the if statement so that code that runs after
            // doesn't use outdated lifetime information. Also, variables can be changed
            // in different ways so we need to re-unify the branches anyway
            foreach (var path in modifiedVars) {
                var oldSig = types.Variables[path];

                // If this variable is changed in both paths, take the max mutation count and add one
                if (iftrueTypes.Variables.Keys.Contains(path) && iffalseTypes.Variables.Keys.Contains(path)) {
                    var trueSig = iftrueTypes.Variables[path];
                    var falseSig = iffalseTypes.Variables[path];

                    var trueLifetime = new Lifetime(trueSig.Path, trueSig.MutationCount, trueSig.IsLifetimeRoot);
                    var falseLifetime = new Lifetime(falseSig.Path, falseSig.MutationCount, falseSig.IsLifetimeRoot);

                    var mutationCount = 1 + Math.Max(
                        trueSig.MutationCount,
                        falseSig.MutationCount);

                    var newSig = new VariableSignature(
                        path, 
                        oldSig.Type, 
                        true, 
                        mutationCount, 
                        oldSig.IsLifetimeRoot);

                    var newLifetime = new Lifetime(path, mutationCount, oldSig.IsLifetimeRoot);

                    newLifetimes.Add(newSig);

                    types.LifetimeGraph.AddBoth(newLifetime, trueLifetime);
                    types.LifetimeGraph.AddBoth(newLifetime, falseLifetime);
                }
                else {
                    // If this variable is changed in only one path
                    int mutationCount;
                    Lifetime oldLifetime;

                    if (iftrueTypes.Variables.ContainsKey(path)) {
                        mutationCount = 1 + iftrueTypes.Variables[path].MutationCount;
                        oldLifetime = new Lifetime(
                            path,
                            iftrueTypes.Variables[path].MutationCount,
                            iftrueTypes.Variables[path].IsLifetimeRoot);
                    }
                    else {
                        mutationCount = 1 + iffalseTypes.Variables[path].MutationCount;
                        oldLifetime = new Lifetime(
                            path,
                            iffalseTypes.Variables[path].MutationCount,
                            iffalseTypes.Variables[path].IsLifetimeRoot);
                    }

                    var newSig = new VariableSignature(
                        path,
                        oldSig.Type,
                        oldSig.IsWritable,
                        mutationCount,
                        oldSig.IsLifetimeRoot);

                    var newLifetime = new Lifetime(path, mutationCount, oldSig.IsLifetimeRoot);

                    newLifetimes.Add(newSig);
                    types.LifetimeGraph.AddBoth(newLifetime, oldLifetime);
                }
            }

            var resultType = types.ReturnTypes[iftrue];
            var result = new IfSyntax(
                this.Location, 
                cond, 
                iftrue, 
                iffalse, 
                resultType,
                this.tempPath,
                newLifetimes.ToValueList());

            types.ReturnTypes[result] = resultType;

            var ifTrueLifetimes = types.Lifetimes[iftrue].ToHashSet();
            var ifFalseLifetimes = types.Lifetimes[iffalse].ToHashSet();

            if (ifTrueLifetimes.SetEquals(ifFalseLifetimes)) {
                types.Lifetimes[result] = ifTrueLifetimes.ToValueList();
            }
            else {
                types.Lifetimes[result] = ifTrueLifetimes
                    .Concat(ifFalseLifetimes)
                    .ToValueList();
            }

            return result;
        }

        public ISyntaxTree ToRValue(SyntaxFrame types) {
            throw new InvalidOperationException();
        }

        public ISyntaxTree ToLValue(SyntaxFrame types) {
            throw new InvalidOperationException();
        }

        public ICSyntax GenerateCode(SyntaxFrame types, ICStatementWriter writer) {
            throw new InvalidOperationException();
        }
    }

    public record IfSyntax : ISyntaxTree {
        private readonly ISyntaxTree cond, iftrue, iffalse;
        private readonly HelixType returnType;
        private readonly IdentifierPath tempPath;
        private readonly ValueList<VariableSignature> newLifetimes;

        public TokenLocation Location { get; }

        public IEnumerable<ISyntaxTree> Children => new[] { this.cond, this.iftrue, this.iffalse };

        public bool IsPure { get; }

        public IfSyntax(TokenLocation loc, ISyntaxTree cond,
                        ISyntaxTree iftrue,
                        ISyntaxTree iffalse, HelixType returnType,
                        IdentifierPath tempPath, 
                        ValueList<VariableSignature> newLifetimes) {

            this.Location = loc;
            this.cond = cond;
            this.iftrue = iftrue;
            this.iffalse = iffalse;
            this.returnType = returnType;
            this.IsPure = cond.IsPure && iftrue.IsPure && iffalse.IsPure;
            this.tempPath = tempPath;
            this.newLifetimes = newLifetimes;
        }

        public ISyntaxTree CheckTypes(SyntaxFrame types) => this;

        public ISyntaxTree ToRValue(SyntaxFrame types) => this;

        public ICSyntax GenerateCode(SyntaxFrame types, ICStatementWriter writer) {
            var affirmList = new List<ICStatement>();
            var negList = new List<ICStatement>();

            var affirmWriter = new CStatementWriter(writer, affirmList);
            var negWriter = new CStatementWriter(writer, negList);

            var affirm = this.iftrue.GenerateCode(types, affirmWriter);
            var neg = this.iffalse.GenerateCode(types, negWriter);

            var tempName = writer.GetVariableName();

            if (this.returnType != PrimitiveType.Void) {
                affirmWriter.WriteStatement(new CAssignment() {
                    Left = new CVariableLiteral(tempName),
                    Right = affirm
                });

                negWriter.WriteStatement(new CAssignment() {
                    Left = new CVariableLiteral(tempName),
                    Right = neg
                });
            }

            var tempStat = new CVariableDeclaration() {
                Type = writer.ConvertType(this.returnType),
                Name = tempName
            };

            var expr = new CIf() {
                Condition = this.cond.GenerateCode(types, writer),
                IfTrue = affirmList,
                IfFalse = negList
            };

            writer.WriteEmptyLine();
            writer.WriteComment($"Line {this.cond.Location.Line}: If statement");

            // Don't bother writing the temp variable if we are returning void
            if (this.returnType != PrimitiveType.Void) {
                writer.WriteStatement(tempStat);
            }

            writer.WriteStatement(expr);

            // Register the lifetime for our return value if we are returning a 
            // pointer or array
            if (this.returnType is PointerType || this.returnType is ArrayType) {
                var lifetime = new Lifetime(this.tempPath, 0, false);

                writer.RegisterLifetime(lifetime, new CMemberAccess() {
                    Target = new CVariableLiteral(tempName),
                    MemberName = "pool"
                });
            }

            // Register all the lifetimes that changed within this if statement
            foreach (var sig in this.newLifetimes) {
                var lifetime = new Lifetime(sig.Path, sig.MutationCount, sig.IsLifetimeRoot);

                writer.RegisterLifetime(lifetime, new CMemberAccess() {
                    Target = new CVariableLiteral(writer.GetVariableName(sig.Path)),
                    MemberName = "pool"
                });
            }

            writer.WriteEmptyLine();

            if (this.returnType != PrimitiveType.Void) {
                return new CVariableLiteral(tempName);
            }
            else {
                return new CIntLiteral(0);
            }
        }
    }
}