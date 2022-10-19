﻿using Trophy.Analysis.Types;
using Trophy.Features;
using Trophy.Features.Aggregates;
using Trophy.Features.Functions;
using Trophy.Features.Variables;
using Trophy.Generation;
using Trophy.Parsing;

namespace Trophy.Analysis {
    public delegate void DeclarationCG(ICWriter writer);

    public class SyntaxFrame {
        private int tempCounter = 0;

        public IdentifierPath CurrentScope { get; }

        public IDictionary<IdentifierPath, FunctionSignature> Functions { get; }

        public IDictionary<IdentifierPath, VariableSignature> Variables { get; }

        public IDictionary<IdentifierPath, AggregateSignature> Aggregates { get; }

        public IDictionary<TrophyType, DeclarationCG> TypeDeclarations { get; }

        public IDictionary<ISyntaxTree, TrophyType> ReturnTypes { get; }

        public IDictionary<IdentifierPath, ISyntaxTree> Trees { get; }

        public SyntaxFrame() {
            this.CurrentScope = new IdentifierPath();

            this.Functions = new Dictionary<IdentifierPath, FunctionSignature>();
            this.Variables = new Dictionary<IdentifierPath, VariableSignature>();
            this.Aggregates = new Dictionary<IdentifierPath, AggregateSignature>();

            this.TypeDeclarations = new Dictionary<TrophyType, DeclarationCG>();
            this.ReturnTypes = new Dictionary<ISyntaxTree, TrophyType>();

            this.Trees = new Dictionary<IdentifierPath, ISyntaxTree>() {
                { new IdentifierPath("void"), new TypeSyntax(default, PrimitiveType.Void) },
                { new IdentifierPath("int"), new TypeSyntax(default, PrimitiveType.Int) },
                { new IdentifierPath("bool"), new TypeSyntax(default, PrimitiveType.Bool) }
            };
        }

        private SyntaxFrame(SyntaxFrame prev, IdentifierPath newScope) {
            this.CurrentScope = newScope;

            this.Functions = new StackedDictionary<IdentifierPath, FunctionSignature>(prev.Functions);
            this.Variables = new StackedDictionary<IdentifierPath, VariableSignature>(prev.Variables);
            this.Aggregates = new StackedDictionary<IdentifierPath, AggregateSignature>(prev.Aggregates);

            this.TypeDeclarations = prev.TypeDeclarations;
            this.ReturnTypes = prev.ReturnTypes;

            this.Trees = new StackedDictionary<IdentifierPath, ISyntaxTree>(prev.Trees);
        }

        public SyntaxFrame WithScope(IdentifierPath newScope) {
            return new SyntaxFrame(this, newScope);
        }

        public SyntaxFrame WithScope(string name) {
            var scope = this.CurrentScope.Append(name);

            return this.WithScope(scope);
        }

        public string GetVariableName() {
            return "$t_" + this.tempCounter++;
        }

        public bool TryResolvePath(string name, out IdentifierPath path) {
            var scope = this.CurrentScope;

            while (true) {
                path = scope.Append(name);
                if (this.Trees.ContainsKey(path)) {
                    return true;
                }

                if (scope.Segments.Any()) {
                    scope = scope.Pop();
                }
                else {
                    return false;
                }
            }
        }

        public IdentifierPath ResolvePath(string path) {
            if (this.TryResolvePath(path, out var value)) {
                return value;
            }

            throw new InvalidOperationException(
                $"Compiler error: The path '{path}' does not contain a value.");
        }

        public bool TryResolveName(string name, out ISyntaxTree value) {
            if (!this.TryResolvePath(name, out var path)) {
                value = null;
                return false;
            }

            return this.Trees.TryGetValue(path, out value);
        }

        public ISyntaxTree ResolveName(string name) {
            return this.Trees[this.ResolvePath(name)];
        }
    }
}