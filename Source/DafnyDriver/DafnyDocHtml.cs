using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;

namespace Microsoft.Dafny;

class DafnyDocHtml {

  public static readonly string eol = "\n";
  public static readonly string br = "<br>";
  public static readonly string mdash = " &mdash; ";
  public static readonly string space4 = "&nbsp;&nbsp;&nbsp;&nbsp;";

  public static string HtmlStart(string title) {
    return
      @"
<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>"

      + title +

@"</title>
<link rel=""icon"" type=""image/png"" href=""dafny-favicon.png"">
<meta name=""description"" content=""Documentation for Dafny code produced by dafnydoc"">
<meta name=""author"" content=""dafnydoc"">
<link rel=""stylesheet"" href=""styles.css"" />
</head>
";
  }

  public static string BodyStart() {
    return "<body>\n";
  }

  public static string BodyAndHtmlEnd() {
    return "</body>\n</html>\n";
  }

  public static string Indent(string text) {
    return $"<p style=\"margin-left: 25px;\">{text}</p>";
  }

  public static string Link(string fullName, string text) {
    return $"<a href=\"{fullName}.html\">{text}</a>";
  }

  public static string Link(string fullName, string inpage, string text) {
    if (fullName == null) {
      return $"<a href=\"#{inpage}\">{text}</a>";
    } else {
      return $"<a href=\"{fullName}.html#{inpage}\">{text}</a>";
    }
  }

  public static string Heading1(string text) {
    return "<div>\n<h1>" + text + "</h1>\n</div>";
  }

  public static string Heading2(string text) {
    return "<div>\n<h2>" + text + "</h2>\n</div>";
  }

  public static string Heading3(string text) {
    return "<div>\n<h3>" + text + "</h3>\n</div>";
  }

  // Used in an h1 heading, but is a lot smaller
  public static string Smaller(string text) {
    return $"<font size=\"-1\">{text}</font>";
  }
  public static string Bold(string text) {
    return $"<b>{text}</b>";
  }

  public static string Italics(string text) {
    return $"<i>{text}</i>";
  }

  public static string Code(string text) {
    return $"<span class=\"code\">{text}</span>";
  }
  public static string Keyword(string text) {
    return Bold(text);
  }

  public static string TableStart() {
    return "<table>";
  }

  public static string Row() {
    return $"<tr></tr>";
  }

  public static string Row(string s1, string s2) {
    return $"<tr><td>{s1}</td><td>{s2}</td></tr>";
  }

  public static string Row(string s1, string s2, string s3) {
    return $"<tr><td>{s1}</td><td>{s2}</td><td>{s3}</td></tr>";
  }

  public static string Row(string s1, string s2, string s3, string s4) {
    return $"<tr><td>{s1}</td><td>{s2}</td><td>{s3}</td><td>{s4}</td></tr>";
  }

  public static string Row(string s1, string s2, string s3, string s4, string s5) {
    return $"<tr><td>{s1}</td><td>{s2}</td><td>{s3}</td><td>{s4}</td><td>{s5}</td></tr>";
  }

  public static string Row(string s1, string s2, string s3, string s4, string s5, string s6) {
    return $"<tr><td>{s1}</td><td>{s2}</td><td>{s3}</td><td>{s4}</td><td>{s5}</td><td>{s6}</td></tr>";
  }

  public static String TableEnd() {
    return "</table>";
  }

  public static String ListStart() {
    return "<ul>";
  }

  public static String ListItem(String text) {
    return "<li>" + text + "</li>";
  }

  public static String ListEnd() {
    return "</ul>";
  }

  public static String Anchor(string name) {
    return $"<a id=\"{name}\"/>";
  }

  public static String LinkToAnchor(string name, string text) {
    return $"<a href=\"#{name}\">{text}</a>";
  }

  public static String RuleWithText(String text) {
    return $"<div style=\"width: 100%; height: 10px; border-bottom: 1px solid black; text-align: center\"><span style=\"font-size: 20px; background-color: #F3F5F6; padding: 0 10px;\">{text}</span></div><br>";
  }

  public static readonly string Style =
  @"
body {
  background-color: white;
  font-family: 'Arial'
}

h1 {
  color: blue;
  text-align: center;
  background-color: #ffec50;
}
h2 {
  color: blue;
  text-align: left;
  background-color: #ffec50;
}
h3 {
  color: blue;
  text-align: left;
  background-color: #fefdcc;
}

p {
  font-size: 16px;
}

span.doctext {
   font-style: italic;
}

span.code {
   font-family: 'Courier New', monospace;
}
";

}