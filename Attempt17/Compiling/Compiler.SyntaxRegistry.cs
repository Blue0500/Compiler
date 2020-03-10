﻿using Attempt17.CodeGeneration;
using Attempt17.Features;
using Attempt17.Parsing;
using Attempt17.TypeChecking;
using System;
using System.Collections.Generic;

namespace Attempt17.Compiling {
    public partial class Compiler {
        private delegate ISyntax<TypeCheckTag> GeneralTypeChecker(ISyntax<ParseTag> syntax, IScope scope, ITypeChecker checker);

        private delegate CBlock GeneralCodeGenerator(ISyntax<TypeCheckTag> syntax, ICScope scope, ICodeGenerator gen);

        private delegate void GeneralScopeModifier(ISyntax<ParseTag> syntax, IScope scope);

        private class SyntaxRegistry : ISyntaxRegistry {
            public Dictionary<Type, GeneralTypeChecker> parseTrees =
                new Dictionary<Type, GeneralTypeChecker>();

            public Dictionary<Type, GeneralCodeGenerator> syntaxTrees =
                new Dictionary<Type, GeneralCodeGenerator>();

            public Dictionary<Type, GeneralScopeModifier> declarations =
                new Dictionary<Type, GeneralScopeModifier>();

            public HashSet<TypeUnifier> unifiers = new HashSet<TypeUnifier>();

            public void RegisterDeclaration<T>(DeclarationScopeModifier<T> scopeModifier) where T : ISyntax<ParseTag> {
                this.declarations.Add(typeof(T), (syntax, scope) => scopeModifier((T)syntax, scope));
            }

            public void RegisterParseTree<T>(SyntaxTypeChecker<T> typeChecker) where T : ISyntax<ParseTag> {
                this.parseTrees.Add(typeof(T), (syntax, scope, checker) => typeChecker((T)syntax, scope, checker));
            }

            public void RegisterSyntaxTree<T>(SyntaxCodeGenerator<T> codeGen) where T : ISyntax<TypeCheckTag> {
                this.syntaxTrees.Add(typeof(T), (syntax, scope, gen) => codeGen((T)syntax, scope, gen));
            }

            public void RegisterTypeUnifier(TypeUnifier unifier) {
                this.unifiers.Add(unifier);
            }
        }
    }
}