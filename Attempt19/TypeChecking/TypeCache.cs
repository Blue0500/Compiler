﻿using System.Collections.Generic;
using Attempt19.Types;

namespace Attempt19.TypeChecking {
    public class ParameterInfo {
        public LanguageType Type { get; }

        public IdentifierPath CapturePath { get; }

        public ParameterInfo(LanguageType type, IdentifierPath capturePath) {
            this.Type = type;
            this.CapturePath = capturePath;
        }
    }

    public class TypeCache {
        public Dictionary<IdentifierPath, VariableInfo> Variables { get; }
            = new Dictionary<IdentifierPath, VariableInfo>();

        public Dictionary<IdentifierPath, FunctionSignature> Functions { get; }
            = new Dictionary<IdentifierPath, FunctionSignature>();

        //public Dictionary<IdentifierPath, StructSignature> Structs { get; }
        //    = new Dictionary<IdentifierPath, StructSignature>();

        public Dictionary<LanguageType, Dictionary<string, IdentifierPath>> Methods { get; }
            = new Dictionary<LanguageType, Dictionary<string, IdentifierPath>>();
    }
}