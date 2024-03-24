﻿namespace Helix.Common.Hmm {
    public interface IHmmVisitor<T> {
        public T VisitDereference(HmmDereference syntax);

        public T VisitIndex(HmmIndex syntax);

        public T VisitAddressOf(HmmAddressOf syntax);

        public T VisitUnaryOperator(HmmUnarySyntax syntax);

        public T VisitBinarySyntax(HmmBinarySyntax syntax);

        public T VisitNew(HmmNewSyntax syntax);

        public T VisitAsSyntax(HmmAsSyntax syntax);

        public T VisitVariableStatement(HmmVariableStatement syntax);

        public T VisitAssignment(HmmAssignment syntax);

        public T VisitIs(HmmIsSyntax syntax);

        public T VisitMemberAccess(HmmMemberAccess syntax);

        public T VisitFunctionDeclaration(HmmFunctionDeclaration syntax);

        public T VisitInvoke(HmmInvokeSyntax syntax);

        public T VisitReturn(HmmReturnSyntax syntax);

        public T VisitBreak(HmmBreakSyntax syntax);

        public T VisitContinue(HmmContinueSyntax syntax);

        public T VisitIfExpression(HmmIfExpression syntax);

        public T VisitLoop(HmmLoopSyntax syntax);

        public T VisitArrayLiteral(HmmArrayLiteral syntax);

        public T VisitStructDeclaration(HmmStructDeclaration syntax);

        public T VisitUnionDeclaration(HmmUnionDeclaration syntax);

        public T VisitTypeDeclaration(HmmTypeDeclaration syntax);

        public T VisitFunctionForwardDeclaration(HmmFunctionForwardDeclaration syntax);
    }
}