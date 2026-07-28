using Xunit;

// Constructor is a cross-language node kind. These real-grammar regressions pin the
// declarations that each grammar can identify without name-based guessing.
public sealed class ConstructorExtractionTests
{
    [Theory]
    [InlineData(
        CodeGraphLanguage.TypeScript,
        "src/Widget.ts",
        "class Widget { constructor() { this.run() } run() {} }",
        "constructor",
        "run")]
    [InlineData(
        CodeGraphLanguage.JavaScript,
        "src/Widget.js",
        "class Widget { constructor() { this.run() } run() {} }",
        "constructor",
        "run")]
    [InlineData(
        CodeGraphLanguage.Java,
        "src/Widget.java",
        "class Widget { Widget() { run(); } void run() {} }",
        "Widget",
        "run")]
    [InlineData(
        CodeGraphLanguage.Php,
        "src/Widget.php",
        "<?php class Widget { public function __construct() {} public function run() {} }",
        "__construct",
        "run")]
    [InlineData(
        CodeGraphLanguage.Ruby,
        "src/widget.rb",
        "class Widget\n  def initialize\n  end\n  def run\n  end\nend\n",
        "initialize",
        "run")]
    [InlineData(
        CodeGraphLanguage.VbNet,
        "src/Widget.vb",
        "Public Class Widget\n  Public Sub New()\n  End Sub\n  Public Sub Run()\n  End Sub\nEnd Class\n",
        "New",
        "Run")]
    [InlineData(
        CodeGraphLanguage.Solidity,
        "src/Widget.sol",
        "contract Widget { constructor() {} function run() public {} }",
        "constructor",
        "run")]
    [InlineData(
        CodeGraphLanguage.Dart,
        "src/widget.dart",
        "class Widget { Widget.named(); void run() {} }",
        "named",
        "run")]
    [InlineData(
        CodeGraphLanguage.Dart,
        "src/widget.dart",
        "class Widget { Widget(); void run() {} }",
        "Widget",
        "run")]
    [InlineData(
        CodeGraphLanguage.Cpp,
        "src/Widget.cpp",
        "class Widget { public: Widget() {} void run() {} };",
        "Widget",
        "run")]
    [InlineData(
        CodeGraphLanguage.Swift,
        "src/Widget.swift",
        "class Widget { init() {} func run() {} }",
        "init",
        "run")]
    [InlineData(
        CodeGraphLanguage.Kotlin,
        "src/Widget.kt",
        "class Widget { constructor(value: Int) fun run() {} }",
        "Widget",
        "run")]
    public void ExplicitConstructors_AreNotMethods(
        string language,
        string filePath,
        string source,
        string constructorName,
        string methodName)
    {
        if (!CodeGraphExpansionHarness.GrammarAvailable(language)) return;

        CodeGraphExtractionResult result =
            CodeGraphExpansionHarness.Extract(language, filePath, source);

        Assert.Contains(
            result.Nodes,
            node => node.Kind == CodeGraphNodeKind.Constructor && node.Name == constructorName);
        Assert.DoesNotContain(
            result.Nodes,
            node => node.Kind == CodeGraphNodeKind.Method && node.Name == constructorName);
        Assert.Contains(
            result.Nodes,
            node => node.Kind == CodeGraphNodeKind.Method && node.Name == methodName);
    }

    [Fact]
    public void Cpp_OutOfLineConstructors_AreClassified()
    {
        if (!CodeGraphExpansionHarness.GrammarAvailable(CodeGraphLanguage.Cpp)) return;

        const string source =
            "class Widget { public: Widget(); void run(); };\n" +
            "Widget::Widget() {}\n" +
            "void Widget::run() {}\n";
        CodeGraphExtractionResult result = CodeGraphExpansionHarness.Extract(
            CodeGraphLanguage.Cpp, "src/Widget.cpp", source);

        Assert.Contains(
            result.Nodes,
            node => node.Kind == CodeGraphNodeKind.Constructor && node.Name == "Widget");
        Assert.Contains(
            result.Nodes,
            node => node.Kind == CodeGraphNodeKind.Method && node.Name == "run");
    }
}
