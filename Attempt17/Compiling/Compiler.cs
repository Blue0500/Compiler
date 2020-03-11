﻿using Attempt17.CodeGeneration;
using Attempt17.Features;
using Attempt17.Features.Arrays;
using Attempt17.Features.Containers;
using Attempt17.Features.FlowControl;
using Attempt17.Features.Functions;
using Attempt17.Features.Primitives;
using Attempt17.Features.Variables;
using Attempt17.Parsing;
using Attempt17.TypeChecking;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Attempt17.Compiling {
    public partial class Compiler {
        private readonly ILanguageFeature[] features = new ILanguageFeature[] { 
            new FlowControlFeature(),
            new FunctionsFeature(),
            new PrimitivesFeature(),
            new VariablesFeature(),
            new ArraysFeature(),
            new ContainersFeature()
        };

        public CompilerResult Compile(string input) {
            var registry = this.GetRegistry();
            var tokens = new Lexer(input).GetTokens();

            var scope = new OuterScope();
            var declFlattener = new DeclarationFlattener(scope);

            var decls = new Parser(tokens)
                .Parse()
                .SelectMany(x => x.Accept(declFlattener))
                .ToArray();

            // Make sure all thedeclarations can add to the scope
            foreach (var decl in decls) {
                registry.declarations[decl.GetType()](decl, scope);
            }

            var typeChecker = new TypeChecker(registry, scope);

            // Type check everything

            var checkedDecls = decls
                .Select(x => typeChecker.Check(x, scope))
                .ToArray();

            var cscope = new OuterCScope(scope.TypeInfo);
            var codegen = new CodeGenerator(registry, cscope);

            // Generate everything
            var lines = checkedDecls
                .Select(x => codegen.Generate(x, cscope))
                .SelectMany(x => x.SourceLines)
                .Prepend("")
                .Prepend("#include <stdlib.h>")
                .Prepend("#include <stdint.h>")
                .ToImmutableList();

            // Get the header text
            var header = new StringBuilder();

            header.AppendLine("#include <stdlib.h>");
            header.AppendLine("#include <stdint.h>");
            header.AppendLine("");

            foreach (var line in codegen.Header1Writer.ToLines()) {
                header.AppendLine(line);
            }

            foreach (var line in codegen.Header2Writer.ToLines()) {
                header.AppendLine(line);
            }

            foreach (var line in codegen.Header3Writer.ToLines()) {
                header.AppendLine(line);
            }

            // Get the source text
            var source = new StringBuilder();

            foreach (var line in lines) {
                source.AppendLine(line);
            }

            return new CompilerResult(header.ToString(), source.ToString());
        }

        private SyntaxRegistry GetRegistry() {
            var registry = new SyntaxRegistry();

            foreach (var feature in this.features) {
                feature.RegisterSyntax(registry);
            }

            return registry;
        }
    }
}