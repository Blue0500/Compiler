﻿using Helix.Analysis;
using Helix.Analysis.Types;
using Helix.Features.Variables;
using Helix.Parsing;

namespace Helix.Features.Functions {
    public static class FunctionsHelper {
        public static void CheckForDuplicateParameters(TokenLocation loc, IEnumerable<string> pars) {
            var dups = pars
                .GroupBy(x => x)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToArray();

            if (dups.Any()) {
                throw TypeCheckingErrors.IdentifierDefined(loc, dups.First());
            }
        }

        public static void DeclareName(FunctionParseSignature sig, SyntaxFrame types) {
            // Make sure this name isn't taken
            if (types.TryResolvePath(sig.Location.Scope, sig.Name, out _)) {
                throw TypeCheckingErrors.IdentifierDefined(sig.Location, sig.Name);
            }

            // Declare this function
            var path = sig.Location.Scope.Append(sig.Name);

            types.SyntaxValues[path] = new TypeSyntax(sig.Location, new NamedType(path));
        }

        public static void DeclareParameters(TokenLocation loc, FunctionSignature sig, SyntaxFrame types) {
            // Declare the parameters
            for (int i = 0; i < sig.Parameters.Count; i++) {
                var parsePar = sig.Parameters[i];
                var type = sig.Parameters[i].Type;
                var path = sig.Path.Append(parsePar.Name);

                if (parsePar.IsWritable) {
                    type = type.ToMutableType();
                }

                // TODO: Revisit this
                if (type is PointerType || type is ArrayType) {
                    var lifetime = new Lifetime(path, 0, true);

                    types.AvailibleLifetimes.Add(lifetime);
                }

                types.Variables[path] = new VariableSignature(path, type, parsePar.IsWritable, 0, true);
                types.SyntaxValues[path] = new VariableAccessSyntax(loc, path);
            }
        }
    }
}