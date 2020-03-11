﻿using Attempt17.Features.Arrays;
using Attempt17.Features.Containers;
using Attempt17.Features.FlowControl;
using Attempt17.Features.Functions;
using Attempt17.Features.Primitives;
using Attempt17.Features.Variables;
using Attempt17.TypeChecking;
using Attempt17.Types;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Attempt17.Parsing {
    public class Parser {
        private int pos = 0;
        private readonly IReadOnlyList<IToken> tokens;

        public Parser(IReadOnlyList<IToken> tokens) {
            this.tokens = tokens;
        }

        public ImmutableList<IParseDeclaration> Parse() {
            var list = ImmutableList<IParseDeclaration>.Empty;

            while (this.pos < tokens.Count) {
                list = list.Add(this.Declaration());
            }

            return list;
        }

        private bool Peek(TokenKind kind) {
            return this.Peek(kind, 1);
        }

        private bool Peek(TokenKind kind, int count) {
            if (this.pos + count - 1 >= this.tokens.Count) {
                return false;
            }

            return this.tokens[this.pos + count - 1].Kind == kind;
        }

        private bool TryAdvance(TokenKind kind) {
            if (this.Peek(kind)) {
                this.Advance(kind);
                return true;
            }

            return false;
        }

        private IToken Advance() {
            if (this.pos >= this.tokens.Count) {
                throw ParsingErrors.EndOfFile(this.tokens.Last().Location);
            }

            return this.tokens[this.pos++];
        }

        private IToken Advance(TokenKind kind) {
            var tok = this.Advance();

            if (tok.Kind != kind) {
                throw ParsingErrors.UnexpectedToken(kind, tok);
            }

            return tok;
        }

        private T Advance<T>() {
            var tok = this.Advance();

            if (!(tok is Token<T> ttok)) {
                throw ParsingErrors.UnexpectedToken(tok);
            }

            return ttok.Value;
        }

        private LanguageType VarTypeExpression() {
            this.Advance(TokenKind.VarKeyword);
            var inner = this.TypeExpression();

            return new VariableType(inner);
        }

        private LanguageType TypeExpression() {
            if (this.Peek(TokenKind.VarKeyword)) {
                return this.VarTypeExpression();
            }

            return this.ArrayTypeExpression();
        }

        private LanguageType ArrayTypeExpression() {
            var start = this.TypeAtom();

            while (this.Peek(TokenKind.OpenBracket)) {
                if (!this.Peek(TokenKind.CloseBracket, 2)) {
                    break;
                }

                this.Advance(TokenKind.OpenBracket);
                this.Advance(TokenKind.CloseBracket);

                start = new ArrayType(start);
            }

            return start;
        }

        private LanguageType TypeAtom() {
            if (this.TryAdvance(TokenKind.IntKeyword)) {
                return IntType.Instance;
            }
            else if (this.TryAdvance(TokenKind.VoidKeyword)) {
                return VoidType.Instance;
            }
            else {
                var path = new IdentifierPath(this.Advance<string>());
                return new NamedType(path);
            }
        }

        private IParseDeclaration FunctionDeclaration() {
            var start = this.Advance(TokenKind.FunctionKeyword);

            string funcName = this.Advance<string>();
            this.Advance(TokenKind.OpenParenthesis);

            var pars = ImmutableList<FunctionParameter>.Empty;
            while (!this.Peek(TokenKind.CloseParenthesis)) {
                var parType = this.TypeExpression();
                var parName = this.Advance<string>();

                if (!this.Peek(TokenKind.CloseParenthesis)) {
                    this.Advance(TokenKind.Comma);
                }

                pars = pars.Add(new FunctionParameter(parName, parType));
            }

            this.Advance(TokenKind.CloseParenthesis);
            this.Advance(TokenKind.YieldSign);

            var returnType = this.TypeExpression();
            var end = this.Advance(TokenKind.Colon);

            var sig = new FunctionSignature(funcName, returnType, pars);
            var body = this.TopExpression();
            var loc = start.Location.Span(end.Location);

            this.Advance(TokenKind.Semicolon);

            return new ParseFunctionDeclaration(
                new ParseTag(loc),
                new FunctionInfo(new IdentifierPath(funcName), sig),
                body);
        }

        private IParseDeclaration StructDeclaration() {
            var first = this.Advance(TokenKind.StructKeyword);

            var name = this.Advance<string>();
            this.Advance(TokenKind.OpenBrace);

            var mems = ImmutableList<StructMember>.Empty;
            var decls = ImmutableList<IParseDeclaration>.Empty;

            while (!this.TryAdvance(TokenKind.CloseBrace)) {
                if (this.Peek(TokenKind.FunctionKeyword) || this.Peek(TokenKind.StructKeyword)) {
                    decls = decls.Add(this.Declaration());
                }
                else {
                    var memType = this.TypeExpression();
                    var memName = this.Advance<string>();

                    this.Advance(TokenKind.Semicolon);

                    mems = mems.Add(new StructMember(memName, memType));
                }
            }

            var last = this.Advance(TokenKind.Semicolon);
            var loc = first.Location.Span(last.Location);
            var tag = new ParseTag(loc);
            var sig = new StructSignature(name, mems);

            return new ParseStructDeclaration(
                tag,
                new StructInfo(sig, new IdentifierPath(name)),
                decls);
        }

        private IParseDeclaration Declaration() {
            if (this.Peek(TokenKind.FunctionKeyword)) {
                return this.FunctionDeclaration();
            }
            else if (this.Peek(TokenKind.StructKeyword)) {
                return this.StructDeclaration();
            }

            throw ParsingErrors.UnexpectedToken(this.Advance());
        }

        private ISyntax<ParseTag> TopExpression() => this.StoreExpression();

        private ISyntax<ParseTag> StoreExpression() {
            var target = this.AsExpression();

            while (true) {
                if (this.TryAdvance(TokenKind.LeftArrow)) {
                    var value = this.TopExpression();
                    var loc = target.Tag.Location.Span(value.Tag.Location);

                    target = new StoreSyntax<ParseTag>(
                        new ParseTag(loc),
                        target,
                        value);
                }
                else if (this.TryAdvance(TokenKind.AssignmentSign)) {
                    if (!(target is ArrayIndexSyntax<ParseTag> syntax)) {
                        throw ParsingErrors.UnexpectedSequence(target.Tag.Location);
                    }

                    var value = this.TopExpression();
                    var loc = target.Tag.Location.Span(value.Tag.Location);
                    var tag = new ParseTag(loc);

                    target = new ArrayStoreSyntax<ParseTag>(
                        tag,
                        syntax.Target,
                        syntax.Index,
                        value);
                }
                else {
                    return target;
                }
            }
        }

        private ISyntax<ParseTag> AsExpression() {
            var target = this.AllocExpression();

            while (this.TryAdvance(TokenKind.AsKeyword)) {
                var type = this.TypeExpression();
                var end = this.tokens[this.pos - 1].Location;

                var loc = target.Tag.Location.Span(end);
                var tag = new ParseTag(loc);

                target = new AsSyntax<ParseTag>(tag, target, type);
            }

            return target;
        }

        private ISyntax<ParseTag> AllocExpression() {
            if (this.Peek(TokenKind.AllocKeyword)) {
                var start = this.Advance(TokenKind.AllocKeyword);
                var target = this.AllocExpression();
                var tag = new ParseTag(start.Location.Span(target.Tag.Location));

                return new AllocSyntax<ParseTag>(tag, target);
            }
            else if (this.Peek(TokenKind.MoveKeyword)) {
                var start = this.Advance(TokenKind.MoveKeyword);
                var kind = this.TryAdvance(TokenKind.LiteralSign) ? MovementKind.LiteralMove : MovementKind.ValueMove;
                var varTok = (Token<string>)this.Advance(TokenKind.Identifier);
                var tag = new ParseTag(start.Location.Span(varTok.Location));

                return new MoveSyntax<ParseTag>(tag, kind, varTok.Value);
            }
            else {
                return this.OrExpression();
            }
        }

        private ISyntax<ParseTag> OrExpression() {
            var first = this.XorExpression();

            while (this.Peek(TokenKind.OrKeyword)) {
                var second = this.XorExpression();
                var loc = first.Tag.Location.Span(second.Tag.Location);

                first = new BinarySyntax<ParseTag>(
                    new ParseTag(loc),
                    BinarySyntaxKind.Or,
                    first,
                    second);
            }

            return first;
        }

        private ISyntax<ParseTag> XorExpression() {
            var first = this.ComparisonExpression();

            while (this.Peek(TokenKind.XorKeyword)) {
                var second = this.ComparisonExpression();
                var loc = first.Tag.Location.Span(second.Tag.Location);

                first = new BinarySyntax<ParseTag>(
                    new ParseTag(loc),
                    BinarySyntaxKind.Xor,
                    first,
                    second);
            }

            return first;
        }

        private ISyntax<ParseTag> ComparisonExpression() {
            var first = this.AndExpression();
            var comparators = new Dictionary<TokenKind, BinarySyntaxKind>() {
                { TokenKind.EqualSign, BinarySyntaxKind.EqualTo }, { TokenKind.NotEqualSign, BinarySyntaxKind.NotEqualTo },
                { TokenKind.LessThanSign, BinarySyntaxKind.LessThan }, { TokenKind.GreaterThanSign, BinarySyntaxKind.GreaterThan },
                { TokenKind.LessThanOrEqualToSign, BinarySyntaxKind.LessThanOrEqualTo },
                { TokenKind.GreaterThanOrEqualToSign, BinarySyntaxKind.GreaterThanOrEqualTo }
            };

            while (true) {
                bool worked = false;

                foreach (var (tok, _) in comparators) {
                    worked |= this.Peek(tok);
                }

                if (!worked) {
                    break;
                }

                var op = this.Advance();
                var second = this.AndExpression();
                var loc = first.Tag.Location.Span(second.Tag.Location);

                first = new BinarySyntax<ParseTag>(
                    new ParseTag(loc),
                    comparators[op.Kind],
                    first,
                    second);
            }

            return first;
        }

        private ISyntax<ParseTag> AndExpression() {
            var first = this.AddExpression();

            while (this.Peek(TokenKind.AndKeyword)) {
                var second = this.AddExpression();
                var loc = first.Tag.Location.Span(second.Tag.Location);

                first = new BinarySyntax<ParseTag>(
                    new ParseTag(loc),
                    BinarySyntaxKind.And,
                    first,
                    second);
            }

            return first;
        }

        private ISyntax<ParseTag> AddExpression() {
            var first = this.MultiplyExpression();

            while (true) {
                if (!this.Peek(TokenKind.AddSign) && !this.Peek(TokenKind.SubtractSign)) {
                    break;
                }

                var tok = this.Advance().Kind;
                var op = tok == TokenKind.AddSign ? BinarySyntaxKind.Add : BinarySyntaxKind.Subtract;
                var second = this.MultiplyExpression();
                var loc = first.Tag.Location.Span(second.Tag.Location);

                first = new BinarySyntax<ParseTag>(
                    new ParseTag(loc),
                    op,
                    first,
                    second);
            }

            return first;
        }

        private ISyntax<ParseTag> MultiplyExpression() {
            var first = this.MemberUsageSyntax();

            while (true) {
                if (!this.Peek(TokenKind.MultiplySign)) {
                    break;
                }

                var tok = this.Advance().Kind;
                var op = tok == TokenKind.MultiplySign ? BinarySyntaxKind.Multiply : BinarySyntaxKind.Multiply;
                var second = this.MemberUsageSyntax();
                var loc = first.Tag.Location.Span(second.Tag.Location);

                first = new BinarySyntax<ParseTag>(
                    new ParseTag(loc),
                    op,
                    first,
                    second);
            }

            return first;
        }

        private ISyntax<ParseTag> MemberUsageSyntax() {
            var first = InvokeExpression();

            if (this.Peek(TokenKind.Dot)) {
                var segs = ImmutableList<IMemberUsageSegment>.Empty;

                while (true) {
                    if (this.TryAdvance(TokenKind.Dot)) {
                        var name = this.Advance<string>();

                        if (this.TryAdvance(TokenKind.OpenParenthesis)) {
                            var args = ImmutableList<ISyntax<ParseTag>>.Empty;

                            while (!this.TryAdvance(TokenKind.CloseParenthesis)) {
                                args = args.Add(this.TopExpression());

                                if (!this.Peek(TokenKind.CloseParenthesis)) {
                                    this.Advance(TokenKind.Comma);
                                }
                            }

                            segs = segs.Add(new MemberInvokeSegment(name, args));
                        }
                        else {
                            segs = segs.Add(new MemberAccessSegment(name));
                        }
                    }
                    else {
                        break;
                    }
                }

                var loc = first.Tag.Location.Span(this.tokens[this.pos - 1].Location);
                var tag = new ParseTag(loc);

                return new MemberUsageParseSyntax(tag, first, segs);
            }
            else {
                return first;
            }
        }

        private ISyntax<ParseTag> InvokeExpression() {
            var first = this.Atom();

            while (true) {
                if (this.TryAdvance(TokenKind.OpenParenthesis)) {
                    var args = ImmutableList<ISyntax<ParseTag>>.Empty;

                    while (!this.Peek(TokenKind.CloseParenthesis)) {
                        args = args.Add(this.TopExpression());

                        if (!this.Peek(TokenKind.CloseParenthesis)) {
                            this.Advance(TokenKind.Comma);
                        }
                    }

                    var last = this.Advance(TokenKind.CloseParenthesis);
                    var loc = first.Tag.Location.Span(last.Location);

                    first = new InvokeParseSyntax(
                        new ParseTag(loc),
                        first,
                        args);
                }
                else if (this.TryAdvance(TokenKind.OpenBracket)) {
                    var inner = this.TopExpression();
                    var last = this.Advance(TokenKind.CloseBracket);
                    var loc = first.Tag.Location.Span(last.Location);
                    var tag = new ParseTag(loc);

                    first = new ArrayIndexSyntax<ParseTag>(tag, first, inner);
                }
                else {
                    return first;
                }
            }
        }

        private ISyntax<ParseTag> VariableDeclarationStatement() {
            var tok = this.Advance(TokenKind.VarKeyword);
            var name = this.Advance<string>();
            var op = this.Advance();

            VariableInitKind kind;

            if (op.Kind == TokenKind.LeftArrow) {
                kind = VariableInitKind.Store;
            }
            else if (op.Kind == TokenKind.AssignmentSign) {
                kind = VariableInitKind.Equate;
            }
            else {
                throw ParsingErrors.UnexpectedToken(op);
            }

            var value = this.TopExpression();
            var loc = tok.Location.Span(value.Tag.Location);

            return new VariableInitSyntax<ParseTag>(
                new ParseTag(loc),
                name,
                kind,
                value);
        }

        private ISyntax<ParseTag> WhileStatement() {
            var start = this.Advance(TokenKind.WhileKeyword);
            var cond = this.TopExpression();

            this.Advance(TokenKind.DoKeyword);
            this.Advance(TokenKind.Colon);

            var body = this.TopExpression();
            var loc = start.Location.Span(body.Tag.Location);

            return new WhileSyntax<ParseTag>(
                new ParseTag(loc),
                cond,
                body);
        }

        public ISyntax<ParseTag> IfStatement() {
            var start = this.Advance(TokenKind.IfKeyword);
            var cond = this.TopExpression();
            var next = this.Advance();

            if (next.Kind == TokenKind.ThenKeyword) {
                this.Advance(TokenKind.Colon);

                var affirm = this.TopExpression();

                this.Advance(TokenKind.ElseKeyword);
                this.Advance(TokenKind.Colon);

                var neg = this.TopExpression();
                var loc = start.Location.Span(neg.Tag.Location);

                return new IfSyntax<ParseTag>(
                    new ParseTag(loc),
                    IfKind.Expression,
                    cond,
                    affirm,
                    Option.Some(neg));
            }
            else if (next.Kind == TokenKind.DoKeyword) {
                this.Advance(TokenKind.Colon);

                var affirm = this.TopExpression();

                if (this.TryAdvance(TokenKind.ElseKeyword)) {
                    this.Advance(TokenKind.Colon);

                    var neg = this.TopExpression();
                    var loc = start.Location.Span(neg.Tag.Location);

                    return new IfSyntax<ParseTag>(
                        new ParseTag(loc),
                        IfKind.Statement,
                        cond,
                        affirm,
                        Option.Some(neg));
                }
                else {
                    var loc = start.Location.Span(affirm.Tag.Location);

                    return new IfSyntax<ParseTag>(
                        new ParseTag(loc),
                        IfKind.Statement,
                        cond,
                        affirm,
                        Option.None<ISyntax<ParseTag>>());
                }
            }
            else {
                throw ParsingErrors.UnexpectedToken(next);
            }
        }

        private ISyntax<ParseTag> Statement() {
            ISyntax<ParseTag> result;

            if (this.Peek(TokenKind.VarKeyword)) {
                result = this.VariableDeclarationStatement();
            }
            else if (this.Peek(TokenKind.IfKeyword)) {
                result = this.IfStatement();
            }
            else if (this.Peek(TokenKind.WhileKeyword)) {
                result = this.WhileStatement();
            }
            else {
                result = this.TopExpression();
            }

            this.Advance(TokenKind.Semicolon);

            return result;
        }

        private ISyntax<ParseTag> IntLiteral() {
            var tok = (Token<long>)this.Advance(TokenKind.IntLiteral);

            return new IntLiteralSyntax<ParseTag>(
                new ParseTag(tok.Location),
                tok.Value);
        }

        private ISyntax<ParseTag> BlockExpression() {
            var start = this.Advance(TokenKind.OpenBrace);
            var list = ImmutableList<ISyntax<ParseTag>>.Empty;

            while (!this.Peek(TokenKind.CloseBrace)) {
                list = list.Add(this.Statement());
            }

            var end = this.Advance(TokenKind.CloseBrace);
            var loc = start.Location.Span(end.Location);

            return new BlockSyntax<ParseTag>(
                new ParseTag(loc),
                list);
        }

        private ISyntax<ParseTag> VariableLiteral() {
            var tok = (Token<string>)this.Advance(TokenKind.Identifier);

            return new VariableAccessParseSyntax(
                new ParseTag(tok.Location),
                VariableAccessKind.ValueAccess,
                tok.Value);
        }

        private ISyntax<ParseTag> VariableLiteralAccessLiteral() {
            var start = this.Advance(TokenKind.LiteralSign);
            var tok = (Token<string>)this.Advance(TokenKind.Identifier);
            var loc = start.Location.Span(tok.Location);

            return new VariableAccessParseSyntax(
                new ParseTag(loc),
                VariableAccessKind.RemoteAccess,
                tok.Value);
        }

        private ISyntax<ParseTag> VoidLiteral() {
            var tok = this.Advance(TokenKind.VoidKeyword);

            return new VoidLiteralSyntax<ParseTag>(new ParseTag(tok.Location));
        }

        private ISyntax<ParseTag> ParenGroup() {
            this.Advance(TokenKind.OpenParenthesis);

            var result = this.TopExpression();

            this.Advance(TokenKind.CloseParenthesis);

            return result;
        }

        private MemberInstantiation<ParseTag> MemberInstantiation() {
            var start = (Token<string>)this.Advance(TokenKind.Identifier);

            this.Advance(TokenKind.AssignmentSign);

            var value = this.TopExpression();

            var loc = start.Location.Span(value.Tag.Location);
            var tag = new ParseTag(loc);

            return new MemberInstantiation<ParseTag>(start.Value, value);
        }

        private ISyntax<ParseTag> NewExpression() {
            var start = this.Advance(TokenKind.NewKeyword);
            var type = this.TypeExpression();

            if (this.TryAdvance(TokenKind.OpenBracket)) {
                // Parse new array
                var count = this.TopExpression();

                this.Advance(TokenKind.CloseBracket);

                var loc = start.Location.Span(count.Tag.Location);
                var tag = new ParseTag(loc);

                return new ArrayRangeLiteralSyntax<ParseTag>(tag, type, count);
            }
            else if (this.TryAdvance(TokenKind.OpenBrace)) {
                // Parse new struct
                var insts = ImmutableList<MemberInstantiation<ParseTag>>.Empty;

                while (!this.Peek(TokenKind.CloseBrace)) {
                    insts = insts.Add(this.MemberInstantiation());

                    if (!this.Peek(TokenKind.CloseBrace)) {
                        this.Advance(TokenKind.Comma);
                    }
                }

                var end = this.Advance(TokenKind.CloseBrace);
                var loc = start.Location.Span(end.Location);
                var tag = new ParseTag(loc);

                return new NewSyntax(tag, type, insts);
            }
            else {
                throw ParsingErrors.UnexpectedToken(this.Advance());
            }
        }

        private ISyntax<ParseTag> BoolLiteral() {
            var start = (Token<bool>)this.Advance(TokenKind.BoolLiteral);
            var tag = new ParseTag(start.Location);

            return new BoolLiteralSyntax<ParseTag>(tag, start.Value);
        }

        private ISyntax<ParseTag> ArrayLiteral() {
            var start = this.Advance(TokenKind.OpenBracket);
            var elems = ImmutableList<ISyntax<ParseTag>>.Empty;

            while (!this.Peek(TokenKind.CloseBracket)) {
                elems = elems.Add(this.TopExpression());

                if (!this.Peek(TokenKind.CloseBracket)) {
                    this.Advance(TokenKind.Comma);
                }
            }

            var end = this.Advance(TokenKind.CloseBracket);
            var loc = start.Location.Span(end.Location);
            var tag = new ParseTag(loc);

            return new ArrayLiteralSyntax<ParseTag>(tag, elems);
        }

        private ISyntax<ParseTag> Atom() {
            if (this.Peek(TokenKind.IntLiteral)) {
                return this.IntLiteral();
            }
            else if (this.Peek(TokenKind.OpenBrace)) {
                return this.BlockExpression();
            }
            else if (this.Peek(TokenKind.Identifier)) {
                return this.VariableLiteral();
            }
            else if (this.Peek(TokenKind.LiteralSign)) {
                return this.VariableLiteralAccessLiteral();
            }
            else if (this.Peek(TokenKind.VoidKeyword)) {
                return this.VoidLiteral();
            }
            else if (this.Peek(TokenKind.OpenParenthesis)) {
                return this.ParenGroup();
            }
            else if (this.Peek(TokenKind.NewKeyword)) {
                return this.NewExpression();
            }
            else if (this.Peek(TokenKind.BoolLiteral)) {
                return this.BoolLiteral();
            }
            else if (this.Peek(TokenKind.OpenBracket)) {
                return this.ArrayLiteral();
            }
            else {
                var next = this.Advance();

                throw ParsingErrors.UnexpectedToken(next);
            }
        }
    }
}