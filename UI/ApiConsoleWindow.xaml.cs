using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace AICAD.UI
{
    public partial class ApiConsoleWindow : Window
    {
        private const int MaxLines = 500;
        public ObservableCollection<CodeLine> Lines { get; } = new ObservableCollection<CodeLine>();

        public ApiConsoleWindow()
        {
            InitializeComponent();
            DataContext = this;

            BtnClear.Click += (_, __) => { try { Lines.Clear(); } catch { } };
            BtnCopy.Click += (_, __) =>
            {
                try
                {
                    if (Lines.Count == 0) return;
                    var joined = string.Join(Environment.NewLine, Lines.Select(l => l.Code));
                    Clipboard.SetText(joined);
                }
                catch { }
            };
        }

        private void CodeBlock_Loaded(object sender, RoutedEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null) return;

            var codeLine = rtb.Tag as CodeLine;
            if (codeLine == null) return;

            try
            {
                rtb.Document.Blocks.Clear();
                var paragraph = new Paragraph { Margin = new Thickness(0) };

                if (codeLine.IsApiCall)
                {
                    HighlightCSharpCode(paragraph, codeLine.Code);
                }
                else
                {
                    paragraph.Inlines.Add(new Run(codeLine.Code) { Foreground = new SolidColorBrush(Color.FromRgb(106, 153, 85)) }); // Comment green
                }

                rtb.Document.Blocks.Add(paragraph);
            }
            catch { }
        }

        private void HighlightCSharpCode(Paragraph paragraph, string code)
        {
            // IDE color scheme (Visual Studio Dark Theme inspired)
            var keywordColor = new SolidColorBrush(Color.FromRgb(86, 156, 214));      // Blue - keywords
            var stringColor = new SolidColorBrush(Color.FromRgb(214, 157, 133));      // Orange - strings
            var numberColor = new SolidColorBrush(Color.FromRgb(181, 206, 168));      // Light green - numbers
            var methodColor = new SolidColorBrush(Color.FromRgb(220, 220, 170));      // Yellow - methods
            var typeColor = new SolidColorBrush(Color.FromRgb(78, 201, 176));         // Cyan - types
            var commentColor = new SolidColorBrush(Color.FromRgb(106, 153, 85));      // Green - comments
            var defaultColor = new SolidColorBrush(Color.FromRgb(212, 212, 212));     // Light gray - default

            var keywords = new[] { "new", "true", "false", "null", "var", "const", "void", "int", "double", "float", "string", "bool", "return" };
            var types = new[] { "ModelDoc2", "SketchManager", "FeatureManager", "ISelectionMgr", "swModel", "swApp", "swView" };

            int i = 0;
            while (i < code.Length)
            {
                // Skip whitespace
                if (char.IsWhiteSpace(code[i]))
                {
                    paragraph.Inlines.Add(new Run(code[i].ToString()) { Foreground = defaultColor });
                    i++;
                    continue;
                }

                // Comment
                if (i < code.Length - 1 && code[i] == '/' && code[i + 1] == '/')
                {
                    var comment = code.Substring(i);
                    paragraph.Inlines.Add(new Run(comment) { Foreground = commentColor });
                    break;
                }

                // String literals
                if (code[i] == '"')
                {
                    var start = i;
                    i++;
                    while (i < code.Length && code[i] != '"')
                    {
                        if (code[i] == '\\' && i + 1 < code.Length) i++; // Skip escaped chars
                        i++;
                    }
                    if (i < code.Length) i++; // Include closing quote
                    paragraph.Inlines.Add(new Run(code.Substring(start, i - start)) { Foreground = stringColor });
                    continue;
                }

                // Numbers
                if (char.IsDigit(code[i]) || (code[i] == '-' && i + 1 < code.Length && char.IsDigit(code[i + 1])))
                {
                    var start = i;
                    if (code[i] == '-') i++;
                    while (i < code.Length && (char.IsDigit(code[i]) || code[i] == '.'))
                        i++;
                    paragraph.Inlines.Add(new Run(code.Substring(start, i - start)) { Foreground = numberColor });
                    continue;
                }

                // Identifiers (keywords, methods, types)
                if (char.IsLetter(code[i]) || code[i] == '_')
                {
                    var start = i;
                    while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
                        i++;
                    
                    var word = code.Substring(start, i - start);
                    Brush color = defaultColor;

                    if (keywords.Contains(word))
                        color = keywordColor;
                    else if (types.Any(t => word.Contains(t)))
                        color = typeColor;
                    else if (i < code.Length && code[i] == '(')
                        color = methodColor;
                    else if (char.IsUpper(word[0]))
                        color = typeColor;

                    paragraph.Inlines.Add(new Run(word) { Foreground = color });
                    continue;
                }

                // Operators and punctuation
                paragraph.Inlines.Add(new Run(code[i].ToString()) { Foreground = defaultColor });
                i++;
            }
        }

        public void AddLine(string line)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(new Action<string>(AddLine), line);
                    return;
                }

                if (string.IsNullOrWhiteSpace(line)) return;
                if (Lines.Count > MaxLines) Lines.RemoveAt(0);
                
                var codeLine = ParseJsonLine(line);
                Lines.Add(codeLine);

                if (ChkAutoscroll.IsChecked == true && Lines.Count > 0)
                {
                    var last = Lines.Last();
                    try { LogList.ScrollIntoView(last); } catch { }
                }
            }
            catch { }
        }

        private CodeLine ParseJsonLine(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                var type = obj["type"]?.ToString() ?? "";
                var data = obj["data"] as JObject;
                
                if (type == "api_call" && data != null)
                {
                    var code = data["code"]?.ToString() ?? "";
                    var description = data["description"]?.ToString() ?? "";
                    return new CodeLine { Code = code, Description = description, IsApiCall = true };
                }
                else
                {
                    var message = data?["message"]?.ToString() ?? json;
                    return new CodeLine { Code = message, IsApiCall = false };
                }
            }
            catch
            {
                return new CodeLine { Code = json, IsApiCall = false };
            }
        }
    }

    public class CodeLine
    {
        public string Code { get; set; }
        public string Description { get; set; }
        public bool IsApiCall { get; set; }
    }
}
