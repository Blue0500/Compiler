﻿using Helix.Analysis;
using Helix.Analysis.Types;
using Helix.Generation;
using Helix.Features.Aggregates;
using Helix.Parsing;
using Helix.Generation.Syntax;
using Helix.Analysis.Flow;
using System.Reflection;
using Helix.Syntax;
using Helix.Analysis.TypeChecking;

namespace Helix.Parsing {
    public partial class Parser {
        private ISyntaxTree MemberAccess(ISyntaxTree first) {
            this.Advance(TokenKind.Dot);

            var tok = this.Advance(TokenKind.Identifier);
            var loc = first.Location.Span(tok.Location);

            return new MemberAccessSyntax(loc, first, tok.Value, default);
        }
    }
}

namespace Helix.Features.Aggregates {
    public record MemberAccessSyntax : ISyntaxTree {
        private static int tempCounter = 0;

        private readonly bool isWritable;
        private readonly IdentifierPath path;

        public ISyntaxTree Target { get; }

        public string MemberName { get; }

        public TokenLocation Location { get; }

        public IEnumerable<ISyntaxTree> Children => new[] { this.Target };

        public bool IsPure => this.Target.IsPure;

        public MemberAccessSyntax(TokenLocation location, ISyntaxTree target, 
                                  string memberName, IdentifierPath scope,
                                  bool isWritable = false) {
            this.Location = location;
            this.Target = target;
            this.MemberName = memberName;
            this.isWritable = isWritable;
            this.path = scope?.Append("$mem" + tempCounter++);
        }

        public ISyntaxTree ToRValue(TypeFrame types) {
            if (!this.IsTypeChecked(types)) {
                throw new InvalidOperationException();
            }

            return this;
        }

        public ISyntaxTree ToLValue(TypeFrame types) {
            if (!this.IsTypeChecked(types)) {
                throw new InvalidOperationException();
            }

            if (!this.isWritable) {
                throw TypeException.LValueRequired(this.Location);
            }

            var target = this.Target.ToLValue(types);
            var result = new MemberAccessLValue(this.Location, target, this.MemberName, this.GetReturnType(types));

            return result.CheckTypes(types);
        }

        public virtual ISyntaxTree CheckTypes(TypeFrame types) {
            if (this.IsTypeChecked(types)) {
                return this;
            }

            var target = this.Target.CheckTypes(types).ToRValue(types);
            var targetType = types.ReturnTypes[target];

            // Handle getting the count of an array
            if (targetType is ArrayType array) {
                if (this.MemberName == "count") {
                    var result = new MemberAccessSyntax(
                        this.Location,
                        target,
                        "count",
                        types.Scope,
                        false);

                    result.SetReturnType(PrimitiveType.Int, types);
                    result.SetCapturedVariables(target, types);
                    result.SetPredicate(target, types);
                    result.SetLifetimes(AnalyzeFlow(this.MemberName, this.path, target, types), types);

                    return result;
                }
            }

            if (targetType.AsStruct(types).TryGetValue(out var sig)) {
                // If this is a struct we can access the fields
                var fieldOpt = sig
                    .Members
                    .Where(x => x.Name == this.MemberName)
                    .FirstOrNone();

                // Make sure this field is present
                if (fieldOpt.TryGetValue(out var field)) {
                    var result = new MemberAccessSyntax(
                        this.Location,
                        target,
                        this.MemberName,
                        types.Scope,
                        field.IsWritable);                    

                    result.SetReturnType(field.Type, types);
                    result.SetCapturedVariables(target, types);
                    result.SetPredicate(target, types);
                    result.SetLifetimes(AnalyzeFlow(this.MemberName, result.path, target, types), types);

                    return result;
                }               
            }

            throw TypeException.MemberUndefined(this.Location, targetType, this.MemberName);
        }

        private static LifetimeBounds AnalyzeFlow(string memName, IdentifierPath path, 
                                                  ISyntaxTree target, TypeFrame flow) {
            var targetLifetimes = target.GetLifetimes(flow);
            var parentLifetimes = flow.DataFlowGraph.GetMemberLifetimes(targetLifetimes.ValueLifetime, memName);

            var memLifetime = new ValueLifetime(
                path, 
                LifetimeRole.Alias, 
                LifetimeOrigin.TempValue);

            flow.LocalLifetimes = flow.LocalLifetimes.SetItem(path, new LifetimeBounds(memLifetime));

            foreach (var parent in parentLifetimes) {
                flow.DataFlowGraph.AddAssignment(parent, memLifetime);
            }

            return new LifetimeBounds(memLifetime);
        }

        public virtual ICSyntax GenerateCode(TypeFrame types, ICStatementWriter writer) {
            if (!this.IsTypeChecked(types)) {
                throw new InvalidOperationException();
            }

            return new CMemberAccess() {
                Target = this.Target.GenerateCode(types, writer),
                MemberName = this.MemberName,
                IsPointerAccess = false
            };
        }
    }

    public record MemberAccessLValue : ISyntaxTree {
        private readonly ISyntaxTree target;
        private readonly string memberName;
        private readonly HelixType memberType;

        public TokenLocation Location { get; }

        public IEnumerable<ISyntaxTree> Children => new[] { this.target };

        public bool IsPure => this.target.IsPure;

        public MemberAccessLValue(TokenLocation location, ISyntaxTree target, 
                                  string memberName, HelixType memberType) {
            this.Location = location;
            this.target = target;
            this.memberName = memberName;
            this.memberType = memberType;
        }

        public ISyntaxTree CheckTypes(TypeFrame types) {
            if (this.IsTypeChecked(types)) {
                return this;
            }

            this.SetReturnType(new PointerType(this.memberType, true), types);
            this.SetLifetimes(this.target.GetLifetimes(types), types);
            this.SetPredicate(types);
            this.SetCapturedVariables(types);

            return this;
        }

        public ICSyntax GenerateCode(TypeFrame types, ICStatementWriter writer) {
            if (!this.IsTypeChecked(types)) {
                throw new InvalidOperationException();
            }

            // Our target will be converted to an lvalue, so we have to dereference it first
            var target = new CPointerDereference() {
                Target = this.target.GenerateCode(types, writer)
            };

            return new CAddressOf() {
                Target = new CMemberAccess() {
                    Target = target,
                    MemberName = this.memberName,
                    IsPointerAccess = false
                }
            };
        }
    }
}
