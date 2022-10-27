﻿using Helix.Parsing;
using System.IO;

namespace Helix.Analysis.Lifetimes {
    public record struct Lifetime(IdentifierPath Path, int MutationCount, bool IsRoot) {

        public Lifetime() : this(new IdentifierPath(), 0, false) { }        
    }
}