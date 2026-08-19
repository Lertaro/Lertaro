using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

[TestClass]
public sealed class ScientificMathParserTests
{
    private static double Parse(string expr) => new ScientificMathParser(expr).Parse();

    [TestMethod]
    [DataRow("2+3", 5.0)]
    [DataRow("10-4", 6.0)]
    [DataRow("6*7", 42.0)]
    [DataRow("10/4", 2.5)]
    [DataRow("10%3", 1.0)]
    public void Parse_BasicArithmetic_ReturnsExpectedResult(string expr, double expected) => Assert.AreEqual(expected, Parse(expr), 1e-9);

    [TestMethod]
    public void Parse_OperatorPrecedence_MultiplicationBeforeAddition() => Assert.AreEqual(14.0, Parse("2+3*4"), 1e-9);

    [TestMethod]
    public void Parse_Parentheses_OverrideOperatorPrecedence() => Assert.AreEqual(20.0, Parse("(2+3)*4"), 1e-9);

    [TestMethod]
    public void Parse_Exponent_IsRightAssociative() =>
        // 2^3^2 = 2^(3^2) = 2^9 = 512, not (2^3)^2 = 64
        Assert.AreEqual(512.0, Parse("2^3^2"), 1e-9);

    [TestMethod]
    public void Parse_UnaryMinus_Negates() => Assert.AreEqual(-5.0, Parse("-5"), 1e-9);

    [TestMethod]
    public void Parse_UnaryPlus_IsNoOp() => Assert.AreEqual(5.0, Parse("+5"), 1e-9);

    [TestMethod]
    public void Parse_DivideByZero_Throws() => Assert.ThrowsExactly<DivideByZeroException>(() => Parse("1/0"));

    [TestMethod]
    public void Parse_HexLiteral_ParsesAsBase16() => Assert.AreEqual(255.0, Parse("0xFF"), 1e-9);

    [TestMethod]
    public void Parse_BinaryLiteral_ParsesAsBase2() => Assert.AreEqual(5.0, Parse("0b101"), 1e-9);

    [TestMethod]
    public void Parse_ScientificNotation_ParsesCorrectly() => Assert.AreEqual(150.0, Parse("1.5e2"), 1e-9);

    [TestMethod]
    public void Parse_ThousandsSeparators_ParsesGroupedNumbers() => Assert.AreEqual(3000.0, Parse("1,000 + 2,000"), 1e-9);

    [TestMethod]
    public void Parse_PiConstant_ReturnsMathPi() => Assert.AreEqual(Math.PI, Parse("pi"), 1e-9);

    [TestMethod]
    public void Parse_PiSymbol_ReturnsMathPi() => Assert.AreEqual(Math.PI, Parse("π"), 1e-9);

    [TestMethod]
    public void Parse_EConstant_ReturnsMathE() => Assert.AreEqual(Math.E, Parse("e"), 1e-9);

    [TestMethod]
    [DataRow("sqrt(16)", 4.0)]
    [DataRow("abs(-5)", 5.0)]
    [DataRow("floor(1.9)", 1.0)]
    [DataRow("ceil(1.1)", 2.0)]
    [DataRow("round(1.4)", 1.0)]
    [DataRow("cbrt(27)", 3.0)]
    [DataRow("log2(8)", 3.0)]
    [DataRow("log10(1000)", 3.0)]
    public void Parse_SingleArgFunctions_ReturnExpectedResult(string expr, double expected) => Assert.AreEqual(expected, Parse(expr), 1e-9);

    [TestMethod]
    public void Parse_SinOfZero_ReturnsZero() => Assert.AreEqual(0.0, Parse("sin(0)"), 1e-9);

    [TestMethod]
    public void Parse_MaxTwoArgFunction_ReturnsLarger() => Assert.AreEqual(7.0, Parse("max(3,7)"), 1e-9);

    [TestMethod]
    public void Parse_ThousandsSeparator_DoesNotConsumeFunctionArgumentSeparator() => Assert.AreEqual(2.0, Parse("min(1,000,2)"), 1e-9);

    [TestMethod]
    public void Parse_InvalidThousandsGrouping_Throws() => Assert.ThrowsExactly<Exception>(() => Parse("1234,567"));

    [TestMethod]
    public void Parse_MinTwoArgFunction_ReturnsSmaller() => Assert.AreEqual(3.0, Parse("min(3,7)"), 1e-9);

    [TestMethod]
    public void Parse_LogWithBaseArgument_UsesGivenBase() => Assert.AreEqual(3.0, Parse("log(8,2)"), 1e-9);

    [TestMethod]
    public void Parse_LogWithoutBaseArgument_DefaultsToLog10() => Assert.AreEqual(2.0, Parse("log(100)"), 1e-9);

    [TestMethod]
    public void Parse_RoundWithPrecisionArgument_RoundsToThatManyDecimals() => Assert.AreEqual(1.23, Parse("round(1.234,2)"), 1e-9);

    [TestMethod]
    public void Parse_UnknownFunction_Throws() => Assert.ThrowsExactly<Exception>(() => Parse("foo(1)"));

    [TestMethod]
    public void Parse_MissingClosingParenthesis_Throws() => Assert.ThrowsExactly<Exception>(() => Parse("(1+2"));

    [TestMethod]
    public void Parse_TrailingGarbage_Throws() => Assert.ThrowsExactly<Exception>(() => Parse("1+2)"));

    [TestMethod]
    public void Parse_EmptyExpression_Throws() => Assert.ThrowsExactly<Exception>(() => Parse(""));

    [TestMethod]
    public void Parse_WhitespaceIsIgnoredBetweenTokens() => Assert.AreEqual(5.0, Parse(" 2 + 3 "), 1e-9);
}
