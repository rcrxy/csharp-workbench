using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Options;
using RoslynBinaryOperatorSpacingOptions = Microsoft.CodeAnalysis.CSharp.Formatting.BinaryOperatorSpacingOptions;
using RoslynCSharpFormattingOptions = Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions;
using RoslynLabelPositionOptions = Microsoft.CodeAnalysis.CSharp.Formatting.LabelPositionOptions;

namespace CSharpWorkbench.Formatting.Core.CSharp.Roslyn;

internal static class RoslynFormattingOptionsMapper
{
    public static OptionSet Apply(OptionSet optionSet, CSharpFormattingOptions options)
    {
        var openBraces = options.CSharpNewLines;
        var spacing = options.CSharpSpacing;
        var indentation = options.CSharpIndentation;
        var parentheses = new HashSet<CSharpParenthesisSpacingContext>(spacing.BetweenParentheses);

        return optionSet
        .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, options.Indentation.Style
            == CSharpIndentationStyle.Tab)
        .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, options.Indentation.TabWidth)
        .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, options.Indentation.Size)
        .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, options.LineEnding)
        .WithChangedOption(RoslynCSharpFormattingOptions.IndentBlock, indentation.IndentBlockContents)
        .WithChangedOption(RoslynCSharpFormattingOptions.IndentBraces, indentation.IndentBraces)
        .WithChangedOption(RoslynCSharpFormattingOptions.IndentSwitchCaseSection, indentation.IndentCaseContents)
        .WithChangedOption(RoslynCSharpFormattingOptions.IndentSwitchSection, indentation.IndentSwitchLabels)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.IndentSwitchCaseSectionWhenBlock,
            indentation.IndentCaseContentsWhenBlock)
        .WithChangedOption(RoslynCSharpFormattingOptions.LabelPositioning, MapLabelPosition(indentation.IndentLabels))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLinesForBracesInAccessors,
            UsesOpenBraceContext(openBraces, CSharpOpenBraceContext.Accessors))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLinesForBracesInAnonymousMethods,
            UsesOpenBraceContext(openBraces, CSharpOpenBraceContext.AnonymousMethods))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLinesForBracesInAnonymousTypes,
            UsesOpenBraceContext(openBraces, CSharpOpenBraceContext.AnonymousTypes))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLinesForBracesInControlBlocks,
            UsesOpenBraceContext(openBraces, CSharpOpenBraceContext.ControlBlocks))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLinesForBracesInLambdaExpressionBody,
            UsesOpenBraceContext(openBraces, CSharpOpenBraceContext.Lambdas))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLinesForBracesInMethods,
            UsesOpenBraceContext(openBraces, CSharpOpenBraceContext.Methods))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLinesForBracesInObjectCollectionArrayInitializers,
            UsesOpenBraceContext(openBraces, CSharpOpenBraceContext.ObjectCollectionArrayInitializers))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLinesForBracesInProperties,
            UsesOpenBraceContext(openBraces, CSharpOpenBraceContext.Properties))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLinesForBracesInTypes,
            UsesOpenBraceContext(openBraces, CSharpOpenBraceContext.Types))
        .WithChangedOption(RoslynCSharpFormattingOptions.NewLineForElse, openBraces.BeforeElse)
        .WithChangedOption(RoslynCSharpFormattingOptions.NewLineForCatch, openBraces.BeforeCatch)
        .WithChangedOption(RoslynCSharpFormattingOptions.NewLineForFinally, openBraces.BeforeFinally)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLineForMembersInObjectInit,
            openBraces.BeforeMembersInObjectInitializers)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLineForMembersInAnonymousTypes,
            openBraces.BeforeMembersInAnonymousTypes)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.NewLineForClausesInQuery,
            openBraces.BetweenQueryExpressionClauses)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceAfterControlFlowStatementKeyword,
            spacing.AfterControlFlowKeyword)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpacingAroundBinaryOperator,
            MapBinaryOperatorSpacing(spacing.AroundBinaryOperators))
        .WithChangedOption(RoslynCSharpFormattingOptions.SpaceAfterComma, spacing.AfterComma)
        .WithChangedOption(RoslynCSharpFormattingOptions.SpaceBeforeComma, spacing.BeforeComma)
        .WithChangedOption(RoslynCSharpFormattingOptions.SpaceAfterSemicolonsInForStatement, spacing.AfterForSemicolon)
        .WithChangedOption(RoslynCSharpFormattingOptions.SpaceBeforeSemicolonsInForStatement, spacing.BeforeForSemicolon)
        .WithChangedOption(RoslynCSharpFormattingOptions.SpaceAfterCast, spacing.AfterCast)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceBeforeColonInBaseTypeDeclaration,
            spacing.BeforeInheritanceColon)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceAfterColonInBaseTypeDeclaration,
            spacing.AfterInheritanceColon)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceAfterMethodCallName,
            spacing.BetweenMethodCallNameAndOpeningParenthesis)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceWithinMethodCallParentheses,
            spacing.BetweenMethodCallParameterListParentheses)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceBetweenEmptyMethodCallParentheses,
            spacing.BetweenMethodCallEmptyParameterListParentheses)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpacingAfterMethodDeclarationName,
            spacing.BetweenMethodDeclarationNameAndOpeningParenthesis)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceWithinMethodDeclarationParenthesis,
            spacing.BetweenMethodDeclarationParameterListParentheses)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceBetweenEmptyMethodDeclarationParentheses,
            spacing.BetweenMethodDeclarationEmptyParameterListParentheses)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceWithinOtherParentheses,
            parentheses.Contains(CSharpParenthesisSpacingContext.ControlFlowStatements))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceWithinExpressionParentheses,
            parentheses.Contains(CSharpParenthesisSpacingContext.Expressions))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.SpaceWithinCastParentheses,
            parentheses.Contains(CSharpParenthesisSpacingContext.TypeCasts))
        .WithChangedOption(
            RoslynCSharpFormattingOptions.WrappingKeepStatementsOnSingleLine,
            options.CSharpWrapping.PreserveSingleLineStatements)
        .WithChangedOption(
            RoslynCSharpFormattingOptions.WrappingPreserveSingleLine,
            options.CSharpWrapping.PreserveSingleLineBlocks);
    }

    private static bool UsesOpenBraceContext(CSharpNewLineOptions options, CSharpOpenBraceContext context)
    {
        return options.BeforeOpenBrace switch
        {
            CSharpOpenBraceMode.All => true,
            CSharpOpenBraceMode.None => false,
            CSharpOpenBraceMode.Selected => options.OpenBraceContexts.Contains(context),
            _ => false,
        };
    }

    private static bool UsesAnyOpenBraceContext(CSharpNewLineOptions options, params CSharpOpenBraceContext[] contexts)
    {
        return contexts.Any(context => UsesOpenBraceContext(options, context));
    }

    private static RoslynLabelPositionOptions MapLabelPosition(CSharpLabelIndentation indentation)
    {
        return indentation switch
        {
            CSharpLabelIndentation.FlushLeft => RoslynLabelPositionOptions.LeftMost,
            CSharpLabelIndentation.OneLessThanCurrent => RoslynLabelPositionOptions.OneLess,
            CSharpLabelIndentation.NoChange => RoslynLabelPositionOptions.NoIndent,
            _ => RoslynLabelPositionOptions.OneLess,
        };
    }

    private static RoslynBinaryOperatorSpacingOptions MapBinaryOperatorSpacing(CSharpBinaryOperatorSpacing spacing)
    {
        return spacing switch
        {
            CSharpBinaryOperatorSpacing.BeforeAndAfter => RoslynBinaryOperatorSpacingOptions.Single,
            CSharpBinaryOperatorSpacing.None => RoslynBinaryOperatorSpacingOptions.Remove,
            CSharpBinaryOperatorSpacing.Ignore => RoslynBinaryOperatorSpacingOptions.Ignore,
            _ => RoslynBinaryOperatorSpacingOptions.Single,
        };
    }
}
