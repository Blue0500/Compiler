﻿using Helix.Analysis;
using Helix.Analysis.Flow;
using Helix.Analysis.TypeChecking;
using Helix.Syntax;
using Helix.Analysis.Types;
using Helix.Features.Primitives;
using Helix.Features.Variables;
using Helix.Generation;
using Helix.Generation.Syntax;
using Helix.Parsing;
using Helix.Features.Functions;

namespace Helix.Parsing {
    public partial class Parser {
        private ISyntaxTree VariableAccess() {
            var tok = this.Advance(TokenKind.Identifier);

            return new VariableAccessParseSyntax(tok.Location, tok.Value);
        }
    }
}

namespace Helix.Features.Variables {
    public record VariableAccessParseSyntax : ISyntaxTree {
        public string Name { get; }

        public TokenLocation Location { get; }

        public IEnumerable<ISyntaxTree> Children => Enumerable.Empty<ISyntaxTree>();

        public bool IsPure => true;

        public VariableAccessParseSyntax(TokenLocation location, string name) {
            this.Location = location;
            this.Name = name;
        }

        public Option<HelixType> AsType(TypeFrame types) {
            // If we're pointing at a type then return it
            if (types.TryResolveName(this.Location.Scope, this.Name, out var syntax)) {
                if (syntax.AsType(types).TryGetValue(out var type)) {
                    return type;
                }
            }

            return Option.None;
        }

        public ISyntaxTree CheckTypes(TypeFrame types) {
            // Make sure this name exists
            if (!types.TryResolvePath(this.Location.Scope, this.Name, out var path)) {
                throw TypeException.VariableUndefined(this.Location, this.Name);
            }

            if (path == new IdentifierPath("void")) {
                return new VoidLiteral(this.Location).CheckTypes(types);
            }

            // See if we are accessing a variable
            if (types.TryGetVariable(path, out var type)) {
                return new VariableAccessSyntax(this.Location, path, type).CheckTypes(types);
            }

            var named = new NominalType(path, NominalTypeKind.Function);
            var funcOpt = types.NominalSupertypes
                .GetValueOrNone(named)
                .SelectMany(x => x.AsFunction(types));

            // See if we are accessing a function
            if (funcOpt.TryGetValue(out var _)) {
                return new FunctionAccessSyntax(this.Location, path).CheckTypes(types);
            }

            throw TypeException.VariableUndefined(this.Location, this.Name);
        }
    }

    public record VariableAccessSyntax : ISyntaxTree {
        private readonly bool isLValue;

        public PointerType VariableSignature { get; }

        public IdentifierPath VariablePath { get; }

        public TokenLocation Location { get; }

        public IEnumerable<ISyntaxTree> Children => Enumerable.Empty<ISyntaxTree>();

        public bool IsPure => true;

        public VariableAccessSyntax(TokenLocation loc, IdentifierPath path, PointerType sig, bool isLValue = false) {
            this.Location = loc;
            this.VariableSignature = sig;
            this.VariablePath = path;
            this.isLValue = isLValue;
        }

        public virtual ISyntaxTree CheckTypes(TypeFrame types) {
            if (this.IsTypeChecked(types)) {
                return this;
            }

            //if (types.TryGetVariable(this.VariablePath, types, out var varType)) {
            this.SetReturnType(this.VariableSignature.InnerType, types);
            //}
            //else if (types.Functions.ContainsKey(this.VariablePath)) {
            //    this.SetReturnType(new NamedType(this.VariablePath), types);
            //}
            //else {
            //    throw new InvalidOperationException("Compiler bug");
            //}

            this.SetCapturedVariables(
                this.VariablePath, 
                VariableCaptureKind.ValueCapture, 
                this.VariableSignature, 
                types);

            this.SetPredicate(types);

            return this;
        }

        public virtual ISyntaxTree ToLValue(TypeFrame types) {
            // Make sure this variable is writable
            if (types.TryGetVariable(this.VariablePath, out var varType) && !varType.IsWritable) { 
                throw TypeException.WritingToConstVariable(this.Location);
            }

            ISyntaxTree result = new VariableAccessSyntax(
                this.Location,
                this.VariablePath,
                this.VariableSignature,
                true);
            
            result = result.CheckTypes(types);

            result.SetReturnType(this.VariableSignature, types);

            result.SetCapturedVariables(
                this.VariablePath, 
                VariableCaptureKind.LocationCapture, 
                this.VariableSignature,
                types);

            return result;
        }

        public ISyntaxTree ToRValue(TypeFrame types) => this;

        public void AnalyzeFlow(FlowFrame flow) {
            var bundleDict = new Dictionary<IdentifierPath, LifetimeBounds>();

            foreach (var (relPath, type) in this.VariableSignature.InnerType.GetMembers(flow)) {
                var memPath = this.VariablePath.AppendMember(relPath);
                var locLifetime = flow.LocalLifetimes[memPath].LocationLifetime;
                var valueLifetime = flow.LocalLifetimes[memPath].ValueLifetime;

#if DEBUG
                if (type.IsValueType(flow) && valueLifetime != Lifetime.None) {
                    throw new Exception("Compiler bug");
                }
#endif

                bundleDict[relPath] = new LifetimeBounds(valueLifetime, locLifetime);
            }

            this.SetLifetimes(new LifetimeBundle(bundleDict), flow);
        }

        public ICSyntax GenerateCode(FlowFrame types, ICStatementWriter writer) {
            ICSyntax result = new CVariableLiteral(writer.GetVariableName(this.VariablePath));

            if (writer.VariableKinds[this.VariablePath] == CVariableKind.Allocated) {
                result = new CPointerDereference() {
                    Target = result
                };
            }

            if (this.isLValue) {
                result = new CAddressOf() {
                    Target = result
                };
            }

            return result;
        }
    }
}