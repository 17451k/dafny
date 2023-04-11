using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;

namespace Microsoft.Dafny;

/* Stuff todo:

- duplicate index entries for traits and classes
- details for iterator
- type characteristics and variance

- newtype, opaque type, datatype, codatatype with members

- import details should distinguish provides and reveals
- not sure import is listing all names

- interpret markdown
- do not include library files
- ability to link to declarations in other documentation sets

- useFullDocstring everywhere

- Dafny.Multiply - empty Types section

Improvements:
- retain source layout of expressions
- make a separate css file
- add Dafny favicon

Refactoring
- possibly combine WriteModule and WriteDecl
- formatIsHtml is not used everywhere - either do so or get rid of it
- replicated code for computing indexlink and contentlink

Questions
- option to maintain source ordering?
- have some options available as module scoped options?
- Should functions show body?
- use table in nameindex?
- add in a cross reference listing?
- list known subtypes of traits?
- omit default decreases?
- use a table for all summary entries?
- modifiers (e.g. ghost) in summary entries?
- overall visual design?
- keep the separation into summary and details?
- improvement to program name title?
- make ghost things italics?
- show full qualified names for extended traits? for RHS of type definitions? for types in signatures?
- option to control writing modification time?

Other
- modules: modifiers, fully qualified refinement
- default exports -- list all available names?
- abstract imports - needs fully qualified target
- in each section , list inherited names, import-opened names
- types - modifiers, show content of definition, link to separate page if there are members
- form of index entries for constructors?

- label implementing functions and methods
- show inherited methods, functions in abstract classes
- show decls from refinement parents

- add attributes everywhere
- make sure we have resolved types

*/

class DafnyDoc {

  public static DafnyDriver.ExitValue DoDocumenting(IList<DafnyFile> dafnyFiles, List<string> dafnyFolders,
    ErrorReporter reporter, string programName, DafnyOptions options) {

    string outputdir = options.DafnyPrintCompiledFile;
    if (outputdir == null) {
      outputdir = DefaultOutputDir;
    }

    // Collect all the dafny files
    var exitValue = DafnyDriver.ExitValue.SUCCESS;
    dafnyFiles = dafnyFiles.Concat(dafnyFolders.SelectMany(folderPath => {
      return Directory.GetFiles(folderPath, "*.dfy", SearchOption.AllDirectories)
          .Select(name => new DafnyFile(name)).ToList();
    })).ToList();
    Console.Out.Write($"Documenting {dafnyFiles.Count} files from {dafnyFolders.Count} folders\n");
    if (dafnyFiles.Count == 0) {
      return exitValue;
    }

    // Do parsing and resolution, obtaining a dafnyProgram
    string err = null;
    Program dafnyProgram = null;
    try {
      err = Dafny.Main.ParseCheck(dafnyFiles, programName, reporter, out dafnyProgram);
    } catch (Exception e) {
      err = "Exception while parsing -- please report the error (use --verbose to see the call stack)";
      if (options.CompileVerbose) {
        Console.Out.WriteLine(e.ToString());
      }
    }
    if (err != null) {
      exitValue = DafnyDriver.ExitValue.DAFNY_ERROR;
      Console.Error.WriteLine(err);
    } else {
      Contract.Assert(dafnyProgram != null);

      // create the output folder if needed
      //if (Directory.Exists(outputdir)) {
      //Directory.Delete(outputdir, true);
      //}
      if (!Directory.Exists(outputdir)) {
        Directory.CreateDirectory(outputdir);
      }
      // Check writable
      try {
        File.Create(outputdir + "/index.html").Dispose();
      } catch (Exception) {
        reporter.Error(MessageSource.Documentation, Token.NoToken, "Insufficient permission to create output files in " + outputdir);
        return DafnyDriver.ExitValue.DAFNY_ERROR;
      }


      // Generate all the documentation
      exitValue = new DafnyDoc(dafnyProgram, reporter, options, outputdir).GenerateDocs(dafnyFiles);

    }
    return exitValue;
  }

  public Program DafnyProgram;
  public ErrorReporter Reporter;
  public DafnyOptions Options;
  public string Outputdir;
  public Dictionary<string, string> nameIndex = new Dictionary<string, string>();
  public bool formatIsHtml = true;

  public DafnyDoc(Program dafnyProgram, ErrorReporter reporter, DafnyOptions options, string outputdir) {
    this.DafnyProgram = dafnyProgram;
    this.Reporter = reporter;
    this.Options = options;
    this.Outputdir = outputdir;
  }

  public DafnyDriver.ExitValue GenerateDocs(IList<DafnyFile> dafnyFiles) {
    try {
      var modDecls = new List<LiteralModuleDecl>();
      var rootModule = DafnyProgram.DefaultModule;
      var decls = rootModule.ModuleDef.TopLevelDecls.Select(d => !(d is LiteralModuleDecl));
      CollectDecls(rootModule, modDecls);
      WriteTOC(modDecls);
      foreach (var m in modDecls) {
        WriteModule(m, dafnyFiles);
      }
      WriteIndex();
      return DafnyDriver.ExitValue.SUCCESS;
    } catch (Exception e) {
      // This is a fail-safe backstop so that dafny itself does not crash
      Reporter.Error(MessageSource.Documentation, DafnyProgram.DefaultModule, $"Unexpected exception while generating documentation: {e.Message}\n{e.StackTrace}");
      return DafnyDriver.ExitValue.DAFNY_ERROR;
    }
  }

  /** Recursively computes a list of all the LiteralModuleDecls declared in the program */
  public void CollectDecls(LiteralModuleDecl mod, List<LiteralModuleDecl> modDecls) {
    modDecls.Add(mod);
    foreach (var d in mod.ModuleDef.TopLevelDecls) {
      if (d is LiteralModuleDecl litmod) {
        CollectDecls(litmod, modDecls);
      }
    }
  }

  /** Writes a doc page for the given module */
  public void WriteModule(LiteralModuleDecl module, IList<DafnyFile> dafnyFiles) {
    var moduleDef = module.ModuleDef;
    var fullName = moduleDef.FullDafnyName;
    var fullNLName = fullName;
    if (moduleDef.IsDefaultModule) {
      nameIndex.Add(rootNLName + " " + nameIndex.Count + " " + rootName, "module " + Link(rootName, rootNLName));
      fullName = rootName;
      fullNLName = rootNLName;
    } else {
      AddToIndexF(module.Name, fullName, "module");
    }
    var defaultClass = moduleDef.TopLevelDecls.First(d => d is ClassDecl cd && cd.IsDefaultClass) as ClassDecl;
    if (formatIsHtml) {
      string filename = Outputdir + "/" + fullName + ".html";
      using StreamWriter file = new(filename);
      file.Write(head1);
      file.Write($"Module {fullName} in program {DafnyProgram.Name}");
      file.Write(head2);
      file.Write(style);
      var abs = moduleDef.IsAbstract ? "abstract " : ""; // The only modifier for modules
      file.WriteLine($"<div>\n<h1>{abs}module {QualifiedNameWithLinks(fullNLName, false)}{space4}{Smaller(contentslink + indexlink)}</h1>\n</div>");
      file.Write(bodystart);

      var docstring = Docstring(module);
      if (!String.IsNullOrEmpty(docstring)) {
        file.Write(ShortAndMoreForDecl(module));
        file.Write(br);
        file.WriteLine(br);
      }
      if (moduleDef.RefinementQId != null) {
        file.WriteLine("refines " + QualifiedNameWithLinks(moduleDef.RefinementQId.ToString()) + br); // TODO - RefinementQID is not fully qualified
      }
      var attributes = Attributes(module.Attributes);
      if (!String.IsNullOrEmpty(attributes)) {
        file.WriteLine("Attributes: " + attributes + br);
      }

      if (moduleDef.IsDefaultModule) {
        if (dafnyFiles.Count != 1) {
          file.WriteLine("From multiple files<br>\n");
        } else {
          file.WriteLine(FileInfo(dafnyFiles[0].CanonicalPath));
        }
      } else {
        file.Write(FileInfo(module.Tok));
      }

      StringBuilder summaries = new StringBuilder(1000);
      StringBuilder details = new StringBuilder(1000);
      WriteExports(moduleDef, summaries, details);
      WriteImports(moduleDef, summaries, details);
      WriteSubModules(moduleDef, summaries, details);
      WriteTypes(moduleDef, summaries, details);
      WriteConstants(defaultClass, summaries, details);
      WriteFunctions(defaultClass, summaries, details);
      WriteMethods(defaultClass, summaries, details);
      WriteLemmas(defaultClass, summaries, details);

      file.WriteLine(Heading2("module summary"));
      file.WriteLine(summaries.ToString());
      file.WriteLine(Anchor("decl-detail"));
      file.WriteLine(Heading2("module details"));
      if (!String.IsNullOrEmpty(docstring)) {
        file.WriteLine(docstring);
        file.WriteLine(br);
      }
      if (!String.IsNullOrEmpty(attributes)) {
        file.WriteLine("Attributes: " + attributes + br);
      }
      file.WriteLine(details.ToString());
      file.Write(foot);
      AnnounceFile(filename);
      var declsWithMembers = moduleDef.TopLevelDecls.Where(c => c is TopLevelDeclWithMembers).Select(c => c as TopLevelDeclWithMembers).ToList();
      foreach (var c in declsWithMembers) {
        if (c is ClassDecl cl && !cl.IsDefaultClass) {
          WriteDecl(c);
        }
      }
    }
  }

  /** Writes files for classes and traits */
  public void WriteDecl(TopLevelDeclWithMembers decl) {
    var fullName = decl.FullDafnyName;
    AddToIndexF(decl.Name, fullName, decl.WhatKind);
    if (formatIsHtml) {
      string filename = Outputdir + "/" + fullName + ".html";
      using StreamWriter file = new(filename);
      file.Write(head1);
      file.Write($"{decl.WhatKind} {fullName}");
      file.Write(head2);
      file.Write(style);
      var extends = "";
      if (decl.ParentTraits != null && decl.ParentTraits.Count() > 0) {
        extends = Smaller(" extends ...");
      }
      var typeparams = TypeFormals(decl.TypeArgs);
      file.WriteLine($"<div>\n<h1>{decl.WhatKind} {QualifiedNameWithLinks(fullName, false)}{typeparams}{extends}{space4}{Smaller(contentslink + indexlink)}</h1>\n</div>");
      file.Write(bodystart);

      var docstring = Docstring(decl as IHasDocstring);
      if (!String.IsNullOrEmpty(docstring)) {
        file.Write(ShortAndMoreForDecl(decl));
        file.Write(br);
        file.Write(br);
        file.Write(eol);
      }

      // Find all traits, transitively
      if (decl.ParentTraits != null && decl.ParentTraits.Count() > 0) {
        extends = String.Join(", ", decl.ParentTraits.Select(t => TypeLink(t)));
        List<Type> todo = new List<Type>();
        List<Type> traits = new List<Type>();
        foreach (var t in decl.ParentTraits) {
          todo.Add(t);
        }
        while (todo.Count != 0) {
          var tt = todo.First();
          todo.RemoveAt(0);
          var tr = ((tt as UserDefinedType).ResolvedClass as NonNullTypeDecl).Class;
          if (!traits.Any(q => q.ToString() == tt.ToString())) {
            if (tr != null && tr.ParentTraits != null) {
              foreach (var t in tr.ParentTraits) {
                todo.Add(t);
              }
            }
            if (!decl.ParentTraits.Any(q => q.ToString() == tt.ToString()) && !traits.Any(q => q.ToString() == tt.ToString())) {
              traits.Add(tt);
            }
          }
        }
        file.Write("Extends traits: " + extends);
        traits.Sort((t, tt) => t.ToString().CompareTo(tt.ToString()));
        var trans = String.Join(", ", traits.Select(t => TypeLink(t)));
        if (!String.IsNullOrEmpty(trans)) {
          file.Write($" [Transitively: {trans}]");
        }
        file.Write(br);
        file.Write(eol);
      }
      // Note: classes and traits do not have modifiers
      var attributes = Attributes(decl.Attributes);
      if (!String.IsNullOrEmpty(attributes)) {
        file.WriteLine("Attributes: " + attributes + br);
      }
      file.Write(FileInfo(decl.Tok));

      StringBuilder summaries = new StringBuilder(1000);
      StringBuilder details = new StringBuilder(1000);
      if (decl is ClassDecl) {
        WriteConstructors(decl, summaries, details);
      }
      WriteConstants(decl, summaries, details);
      WriteMutableFields(decl, summaries, details);
      WriteFunctions(decl, summaries, details);
      WriteMethods(decl, summaries, details);
      WriteLemmas(decl, summaries, details);

      file.WriteLine(Heading2(decl.WhatKind + " summary"));

      file.WriteLine(summaries.ToString());
      file.WriteLine(Anchor("decl-detail"));
      file.WriteLine(Heading2(decl.WhatKind + " details"));
      if (!String.IsNullOrEmpty(docstring)) {
        file.WriteLine(docstring);
        file.WriteLine(br);
      }
      if (!String.IsNullOrEmpty(attributes)) {
        file.WriteLine("Attributes: " + attributes + br);
      }
      file.WriteLine(details.ToString());
      file.Write(foot);
      AnnounceFile(filename);
    }
  }

  /** Returns printable info about the file containing the given token and the last modification time of the file */
  public string FileInfo(IToken tok) {
    if (tok != null) {
      return FileInfo(tok.Filename);
    }
    return "";
  }

  public string FileInfo(string filename) {
    string declFilename = GetFileReference(filename);
    if (declFilename != null) {
      var modifyTime = File.GetLastWriteTime(filename);
      var result = $"From file: {declFilename}{br}\n";
      if (Options.DocShowModifyTime) {
        result += $"Last modified: {modifyTime}{br}\n";
      }
      return result;
    }
    return "";
  }

  /** Massages a filename into the form requested by the --doc-file-name option */
  public string GetFileReference(string absoluteFile) {
    var r = Options.DocFilenameFormat;
    if (r == null || r == "name") {
      return Path.GetFileName(absoluteFile);
    } else if (r == "none") {
      return null;
    } else if (r == "absolute") {
      return absoluteFile;
    } else if (r.StartsWith("relative")) { // Allow either relative: or relative=
      var prefix = r.Substring("relative".Length + 1);
      return Path.GetRelativePath(prefix, absoluteFile);
    } else {
      // Default or unrecognized value
      return Path.GetFileName(absoluteFile);
    }
  }

  /** Append the summary and detail information about exports to the string builders */
  public void WriteExports(ModuleDefinition module, StringBuilder summaries, StringBuilder details) {

    var exports = module.TopLevelDecls.Where(d => d is ModuleExportDecl).Select(d => d as ModuleExportDecl);
    if (exports.Count() > 0) {
      summaries.Append(Heading3("Export sets")).Append(eol);
      details.Append(Heading3("Export sets")).Append(eol);
      foreach (var ex in exports) {
        AddToIndex(ex.Name, module.FullDafnyName, "export set");
        var text = $"export {module.Name}`{LinkToAnchor(ExportSetAnchor(ex.Name), Bold(ex.Name))}";
        summaries.Append(text).Append(DashShortDocstring(ex)).Append(br).Append(eol);

        details.Append(Anchor(ExportSetAnchor(ex.Name))).Append(eol);
        details.Append(RuleWithText(ex.Name)).Append(eol);
        var extends = String.Join(", ", ex.Extends.Select(e => LinkToAnchor(ExportSetAnchor(e.val), e.val)).ToList());
        if (ex.Extends.Count > 0) {
          extends = " extends " + extends;
        }
        details.Append(text).Append(extends).Append(br).Append(eol);
        var revealed = ex.Exports.Where(e => !e.Opaque).Select(e => e.Id).ToList();
        revealed.Sort();
        var provided = ex.Exports.Where(e => e.Opaque).Select(e => e.Id).ToList();
        provided.Sort();
        details.Append(space4).Append("provides");
        if (ex.ProvideAll) details.Append(" * :");
        foreach (var id in provided) { // TODO - does not work for links that go out of the file, e.g. classes, modules
          details.Append(" ").Append(LinkToAnchor(id, Bold(id)));
        }
        details.Append(br).Append(eol);
        details.Append(space4).Append("reveals");
        if (ex.RevealAll) details.Append(" * :");
        foreach (var id in revealed) { // TODO - ditto
          details.Append(" ").Append(LinkToAnchor(id, Bold(id)));
        }
        var docstring = Docstring(ex);
        if (!String.IsNullOrEmpty(docstring)) {
          details.Append(Indented(docstring));
        }
        details.Append(br).Append(eol);
      }
    }
  }

  /* Export sets are in a different namespace from other declarations in a module, so they
     might have the same name as another declaration. So we mangle an export set slightly
    so that there will be no duplicate anchor names. */
  public string ExportSetAnchor(string name) {
    return name + "+";
  }

  /** Append the summary and detail information about imports to the string builders */
  public void WriteImports(ModuleDefinition module, StringBuilder summaries, StringBuilder details) {
    var imports = module.TopLevelDecls.Where(d => d is AliasModuleDecl).Select(d => d as AliasModuleDecl).ToList();
    imports.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    var absimports = module.TopLevelDecls.Where(d => d is AbstractModuleDecl).Select(d => d as AbstractModuleDecl).ToList();
    absimports.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    if (imports.Count() + absimports.Count() > 0) {
      summaries.Append(Heading3("Imports")).Append(eol);
      details.Append(Heading3("Imports")).Append(eol);
      foreach (var imp in imports) {
        var name = imp.Name;
        var target = imp.Dereference();
        var exportsets = String.Join(", ", imp.Exports.Select(e => Link(target.FullDafnyName, ExportSetAnchor(e.val), e.val)));
        if (exportsets.Length == 0) {
          exportsets = Link(target.FullDafnyName, ExportSetAnchor(target.Name), target.Name);
        }
        summaries.Append($"import {LinkToAnchor(name, Bold(name))} = {QualifiedNameWithLinks(target.FullDafnyName)}`{exportsets}").Append(br).Append(eol);

        details.Append(Anchor(name)).Append(eol);
        details.Append(RuleWithText(imp.Name)).Append(eol);
        details.Append("import ").Append(Bold(imp.Opened ? "IS " : "IS NOT ")).Append("opened").Append(br).Append(eol);
        details.Append("Names imported: ");
        var list = imp.AccessibleSignature(true).StaticMembers.Values.ToList();
        list.Sort((a, b) => a.Name.CompareTo(b.Name));
        foreach (var d in list) {
          string link;
          if (HasOwnPage(d)) {
            link = Link(d.FullDafnyName, d.Name, d.Name);
          } else {
            string fullname = d.FullDafnyName;
            var k = fullname.LastIndexOf('.');
            if (k < 0) {
              // If there is no parent segment, this should be its own page
              link = Link(d.FullDafnyName, d.Name, d.Name);
            } else {
              link = Link(fullname.Substring(0, k), d.Name, d.Name);
            }
          }
          details.Append(" ").Append(link);
        }
        details.Append(br).Append(eol);
      }
      foreach (var imp in absimports) {
        var name = imp.Name;
        var target = imp.QId; // TODO - is this fully quallified
        summaries.Append($"import {name} : {QualifiedNameWithLinks(target.ToString())}").Append(eol);
      }
    }
  }

  /** Append the summary information about nested module declarations to the string builders;
      the detail information is on a different html page. */
  public void WriteSubModules(ModuleDefinition module, StringBuilder summaries, StringBuilder details) {
    var submods = module.TopLevelDecls.Where(d => d is LiteralModuleDecl).Select(d => d as LiteralModuleDecl).ToList();
    submods.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    if (submods.Count() > 0) {
      summaries.Append(Heading3("Submodules")).Append(eol);
      foreach (var submod in submods) {
        summaries.Append("module ").Append(QualifiedNameWithLinks(submod.FullDafnyName));
        summaries.Append(DashShortDocstring(submod));
        summaries.Append(br).Append(eol);
      }
    }
  }

  public bool IsType(TopLevelDecl t) {
    return (t is RevealableTypeDecl || t is SubsetTypeDecl);
  }

  /** Append the summary and detail information about type declarations to the string builders */
  public void WriteTypes(ModuleDefinition module, StringBuilder summaries, StringBuilder details) {
    var types = module.TopLevelDecls.Where(c => IsType(c)).ToList();
    types.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    if (types.Count() > 0) {
      summaries.Append(Heading3("Types")).Append(eol);
      details.Append(Heading3("Types")).Append(eol);
      summaries.Append(BeginTable());
      foreach (var t in types) {
        if ((t is ClassDecl) && (t as ClassDecl).IsDefaultClass) {
          continue;
        }
        AddToIndex(t.Name, module.FullDafnyName, t.WhatKind);
        var docstring = t is IHasDocstring ? Docstring(t as IHasDocstring) : "";
        var attributes = Attributes(t.Attributes);
        // Note: Types do not have modifiers (at least at present)
        var modifiers = "";
        var typeparams = TypeFormals(t.TypeArgs);
        var link = "";
        if (HasOwnPage(t)) {
          link = Link(t.FullDafnyName, Bold(t.Name));
        } else {
          link = LinkToAnchor(t.Name, Bold(t.Name));
        }
        summaries.Append(Row(t.WhatKind, link + typeparams, DashShortDocstring(t as IHasDocstring))).Append(eol);

        details.Append(Anchor(t.Name)).Append(eol);
        details.Append(RuleWithText(t.Name)).Append(eol);
        if (!String.IsNullOrEmpty(modifiers)) {
          details.Append(modifiers).Append(br).Append(eol);
        }
        details.Append(t.WhatKind).Append(" ").Append(Bold(t.Name)).Append(TypeFormals(t.TypeArgs));
        if (t is ClassDecl) { // Class, Trait
          details.Append(Mdash).Append("see ").Append(Link(t.FullDafnyName, "separate page here"));
        } else if (t is SubsetTypeDecl ts) {
          // TODO witness
          details.Append(" = ").Append(ts.Var.Name).Append(": ").Append(TypeLink(ts.Var.Type)).Append(" | ").Append(ts.Constraint.ToString());
        } else if (t is TypeSynonymDecl tsy) {
          details.Append(" = ").Append(TypeLink(tsy.Rhs));
        } else if (t is NewtypeDecl tnt) {
          if (tnt.Var != null) {
            // TODO witness
            details.Append(" = ").Append(tnt.Var.Name).Append(": ").Append(TypeLink(tnt.Var.Type)).Append(" | ").Append(tnt.Constraint.ToString());
          } else {
            details.Append(" = ").Append(TypeLink(tnt.BaseType));
          }
        } else if (t is OpaqueTypeDecl) {
          // do nothing
        } else if (t is DatatypeDecl) {
          details.Append(br).Append(eol);
          var dt = t as DatatypeDecl;
          details.Append(BeginTable());
          foreach (var ctor in dt.Ctors) {
            string sig = ctor.Name;
            if (ctor.Formals.Count > 0) {
              sig += "(" + String.Join(", ", ctor.Formals.Select(ff => TypeString(ff))) + ")";
            }
            var ds = Docstring(ctor);
            string info;
            if (String.IsNullOrEmpty(ds)) {
              info = "";
            } else if (ds == Shorten(ds)) {
              info = ShortDocstring(ctor);
            } else {
              info = Indented(ds, true);
            }
            details.Append(Row(space4, ctor.IsGhost ? "[ghost]" : "", sig, info == "" ? "" : Mdash, info));
          }
          details.Append(EndTable());
        } else {
          Reporter.Warning(MessageSource.Documentation, null, t.Tok, "Kind of type not handled in dafny doc");
        }
        if (!String.IsNullOrEmpty(attributes)) {
          details.Append(br).Append(eol);
          details.Append(space4).Append(attributes);
        }
        if (!String.IsNullOrEmpty(docstring)) {
          details.Append(Indented(docstring));
        }
      }
      summaries.Append(EndTable());
    }
  }

  /** Append the summary and detail information about const declarations to the string builders */
  public void WriteConstants(TopLevelDeclWithMembers decl, StringBuilder summaries, StringBuilder details) {
    var constants = decl.Members.Where(c => c is ConstantField).Select(c => c as ConstantField).ToList();
    constants.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    if (constants.Count() > 0) {
      summaries.Append(Heading3("Constants\n"));
      details.Append(Heading3("Constants\n"));
      // TODO: opaque, ghost,  

      foreach (var c in constants) {
        AddToIndex(c.Name, decl.FullDafnyName, "const");
        var docstring = Docstring(c);
        var modifiers = Modifiers(c);
        var attributes = Attributes(c.Attributes);
        summaries.Append(LinkToAnchor(c.Name, Bold(c.Name))).Append(": ").Append(TypeLink(c.Type));

        if (!String.IsNullOrEmpty(docstring)) {
          summaries.Append(DashShortDocstring(c));
        }
        summaries.Append(br).Append(eol);

        details.Append(Anchor(c.Name)).Append(eol);
        details.Append(RuleWithText(c.Name)).Append(eol);
        if (!String.IsNullOrEmpty(modifiers)) {
          details.Append(modifiers).Append(br).Append(eol);
        }
        details.Append(Bold(c.Name)).Append(": ").Append(TypeLink(c.Type));
        if (c.Rhs != null) {
          details.Append(" := ").Append(c.Rhs.ToString()); // TODO - other kinds of initialization?
        }
        details.Append(br).Append(eol);
        if (!String.IsNullOrEmpty(attributes)) {
          details.Append(space4).Append(attributes).Append(br).Append(eol);
        }
        details.Append(Indented(docstring));
      }
    }
  }

  /** Append the summary and detail information about field declarations to the string builders */
  public void WriteMutableFields(TopLevelDeclWithMembers decl, StringBuilder summaries, StringBuilder details) {
    var fields = decl.Members.Where(c => c is Field && c is not ConstantField).Select(c => c as Field).ToList();
    fields.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    if (fields.Count() > 0) {
      summaries.Append(Heading3("Mutable Fields\n"));
      details.Append(Heading3("Mutable Fields\n"));

      summaries.Append(BeginTable()).Append(eol);
      foreach (var c in fields) {
        AddToIndex(c.Name, decl.FullDafnyName, "var");

        summaries.Append(Row(LinkToAnchor(c.Name, Bold(c.Name)), ":", TypeLink(c.Type), DashShortDocstring(c))).Append(eol);

        var modifiers = Modifiers(c);
        var attrs = Attributes(c.Attributes);
        details.Append(Anchor(c.Name)).Append(eol);
        details.Append(RuleWithText(c.Name)).Append(eol);
        if (!String.IsNullOrEmpty(modifiers)) {
          details.Append(modifiers).Append(br).Append(eol);
        }
        details.Append(Bold(c.Name)).Append(": ").Append(TypeLink(c.Type)).Append(br).Append(eol);
        if (!String.IsNullOrEmpty(attrs)) {
          details.Append(space4).Append(attrs).Append(br).Append(eol);
        }
        details.Append(Indented(Docstring(c)));
      }
      summaries.Append(EndTable()).Append(eol);
    }
  }

  public void WriteFunctions(TopLevelDeclWithMembers decl, StringBuilder summaries, StringBuilder details) {
    var functions = decl.Members.Where(m => m is Function).Select(m => m as MemberDecl).ToList();
    functions.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    if (functions.Count > 0) {
      summaries.Append(Heading3("Functions")).Append(eol);
      details.Append(Heading3("Functions")).Append(eol);
      WriteMethodsList(functions, decl, summaries, details);
    }
  }

  public void WriteMethods(TopLevelDeclWithMembers decl, StringBuilder summaries, StringBuilder details) {
    var methods = decl.Members.Where(m => m is Method && !(m as Method).IsLemmaLike && !(m is Constructor)).Select(m => m as MemberDecl).ToList();
    methods.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    if (methods.Count() > 0) {
      summaries.Append(Heading3("Methods")).Append(eol);
      details.Append(Heading3("Methods")).Append(eol);
      WriteMethodsList(methods, decl, summaries, details);
    }
  }

  public void WriteConstructors(TopLevelDeclWithMembers decl, StringBuilder summaries, StringBuilder details) {
    var methods = decl.Members.Where(m => m is Constructor).Select(m => m as MemberDecl).ToList();
    methods.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    if (methods.Count() > 0) {
      summaries.Append(Heading3("Constructors")).Append(eol);
      details.Append(Heading3("Constructors")).Append(eol);
      WriteMethodsList(methods, decl, summaries, details);
    }
  }

  string MethodSig(MemberDecl m) {
    if (m is Method) {
      var mth = m as Method;
      var typeparams = TypeFormals(mth.TypeArgs);
      var formals = String.Join(", ", mth.Ins.Select(f => TypeString(f)));
      var outformals = mth.Outs.Count == 0 ? "" :
        " returns (" + String.Join(", ", mth.Outs.Select(f => TypeString(f))) + ")";
      return Bold(m.Name) + typeparams + "(" + formals + ")" + outformals;
    } else if (m is Function) {
      var f = m as Function;
      var typeparams = TypeFormals(f.TypeArgs);
      var formals = String.Join(", ", f.Formals.Select(ff => TypeString(ff)));
      return Bold(m.Name) + typeparams + "(" + formals + "): " + TypeLink(f.ResultType);
    } else {
      return "";
    }
  }

  string TypeString(Formal ff) { // TODO - need modifiers
    return ff.Name + ": " + TypeLink(ff.Type);
  }

  // For methods, lemmas, functions
  public void WriteMethodsList(List<MemberDecl> members, TopLevelDeclWithMembers decl, StringBuilder summaries, StringBuilder details) {
    foreach (var m in members) {
      var md = m as IHasDocstring;
      var ms = MethodSig(m);
      var docstring = Docstring(md);
      var modifiers = Modifiers(m);
      var attributes = Attributes(m.Attributes);
      var name = m.Name;
      if (m is Constructor) {
        if (name == "_ctor") {
          name = decl.Name;
          AddToIndexC(name, "_", decl.FullDafnyName, m.WhatKind);
        } else {
          name = decl.Name + "." + m.Name;
          AddToIndexC(name, m.Name, decl.FullDafnyName, m.WhatKind);
        }
      } else {
        AddToIndex(name, decl.FullDafnyName, m.WhatKind);
      }

      String link = $"<a href=\"#{name}\">{name}</a>"; // TODO - use some Link method
      String mss = ms.ReplaceFirst(m.Name, link); // TODO - don't use extension

      summaries.Append(mss);
      if (!String.IsNullOrEmpty(docstring)) {
        summaries.Append(space4).Append(DashShortDocstring(md));
      }
      summaries.Append(br).Append(eol);

      details.Append(Anchor(name)).Append(eol);
      details.Append(RuleWithText(name)).Append(eol);
      if (!String.IsNullOrEmpty(modifiers)) {
        details.Append(modifiers).Append(br).Append(eol);
      }
      details.Append(m.WhatKind).Append(br).Append(eol);
      mss = ms.ReplaceFirst(m.Name, Bold(name));
      details.Append(mss).Append(br).Append(eol);

      if (!String.IsNullOrEmpty(attributes)) {
        details.Append(space4).Append(attributes).Append(br).Append(eol);
      }
      details.Append(Indented(docstring));
      AppendSpecs(details, m);
    }
  }

  public void WriteLemmas(TopLevelDeclWithMembers decl, StringBuilder summaries, StringBuilder details) {
    var methods = decl.Members.Where(m => m is Method && (m as Method).IsLemmaLike).Select(m => m as MemberDecl).ToList();
    methods.Sort((f, ff) => f.Name.CompareTo(ff.Name));
    if (methods.Count() > 0) {
      summaries.Append(Heading3("Lemmas")).Append(eol);
      details.Append(Heading3("Lemmas")).Append(eol);
      WriteMethodsList(methods, decl, summaries, details);
    }
  }

  public string Indented(string docstring) {
    return Indented(docstring, false);
  }

  public string Indented(string docstring, bool nothingIfNull) {
    if (!String.IsNullOrEmpty(docstring)) {
      return indent + docstring + unindent + eol;
    } else if (!nothingIfNull) {
      return br + eol;
    } else {
      return "";
    }
  }

  // returns true iff some specs were appended to the StringBuilder
  public bool AppendSpecs(StringBuilder details, MemberDecl d) {
    bool some = false;
    if (d is Method) {
      var m = d as Method;
      foreach (var req in m.Req) {
        details.Append(space4).Append("<b>requires</b> ").Append(req.E.ToString()).Append(br).Append(eol);
        some = true;
      }
      //foreach (var mod in m.Mod) {
      // TODO details.Append(space4).Append("modifies ").Append(mod.ToString()).Append(br).Append(eol);
      // some = true;
      //}
      foreach (var en in m.Ens) {
        details.Append(space4).Append("<b>ensures</b> ").Append(en.E.ToString()).Append(br).Append(eol);
        some = true;
      }
      if (m.Decreases != null && m.Decreases.Expressions.Count > 0) {
        var dec = String.Join(", ", m.Decreases.Expressions.Select(e => e.ToString()));
        details.Append(space4).Append("<b>decreases</b> ").Append(dec).Append(br).Append(eol);
        some = true;
      }
    } else if (d is Function) {
      var m = d as Function;
      foreach (var req in m.Reads) {
        details.Append(space4).Append("<b>reads</b> ").Append(req.E.ToString()).Append(br).Append(eol);
        some = true;
      }
      foreach (var req in m.Req) {
        details.Append(space4).Append("<b>requires</b> ").Append(req.E.ToString()).Append(br).Append(eol);
        some = true;
      }
      foreach (var en in m.Ens) {
        details.Append(space4).Append("<b>ensures</b> ").Append(en.E.ToString()).Append(br).Append(eol);
        some = true;
      }
      if (m.Decreases != null && m.Decreases.Expressions.Count > 0) {
        var dec = String.Join(", ", m.Decreases.Expressions.Select(e => e.ToString()));
        details.Append(space4).Append("<b>decreases</b> ").Append(dec).Append(br).Append(eol);
        some = true;
      }
    }
    return some;
  }

  public void WriteTOC(List<LiteralModuleDecl> modules) {
    modules.Sort((k, m) => k.FullDafnyName.CompareTo(m.FullDafnyName));
    if (formatIsHtml) {
      string filename = Outputdir + "/index.html";
      using StreamWriter file = new(filename);
      file.Write(head1);
      file.Write($"Dafny Documentation{ProgramHeader()}");
      file.Write(head2);
      file.Write(style);
      var indexlink = Link("nameindex", "[index]");

      file.Write($"<div>\n<h1>Modules{ProgramHeader()}{space4}{Smaller(indexlink)}</h1>\n</div>");
      file.Write(bodystart);
      file.WriteLine("<ul>");
      int currentIndent = 0;
      foreach (var module in modules) {
        var fullname = module.FullDafnyName;
        int level = Regex.Matches(fullname, "\\.").Count;
        while (level > currentIndent) {
          file.WriteLine("<ul>");
          currentIndent++;
        }
        while (level < currentIndent) {
          file.WriteLine("</ul>");
          currentIndent--;
        }
        var ds = DashShortDocstringNoMore(module);
        if (module.ModuleDef.IsDefaultModule) {
          file.WriteLine($"<li>Module <a href=\"{rootName}.html\">{rootNLName}</a>{ds}</li>");
        } else {
          file.WriteLine($"<li>Module <a href=\"{fullname}.html\">{fullname}</a>{ds}</li>");
        }
      }
      file.WriteLine("</ul>");
      file.Write(foot);
      AnnounceFile(filename);
    }
  }

  public static readonly string rootName = "_"; // Name of file for root module
  public static readonly string rootNLName = " (root module)"; // Name of root module to display

  public string ProgramHeader() {
    var programName = DafnyProgram.Options.DocProgramNameOption;
    return programName == null ? "" : (" for " + programName);
  }

  public string TypeLink(Type t) {
    if (t is BasicType) {
      return t.ToString();
    } else if (t is CollectionType ct) {
      return ct.CollectionTypeName + TypeActualParameters(ct.TypeArgs);
    } else if (t is UserDefinedType udt) {
      var tt = udt.ResolvedClass;
      if (tt is ClassDecl) {
        return Link(tt.FullDafnyName, tt.Name) + TypeActualParameters(t.TypeArgs);
      } else if (tt is NonNullTypeDecl) {
        return Link(tt.FullDafnyName, tt.Name) + TypeActualParameters(t.TypeArgs);
      } else if (tt is SubsetTypeDecl) {
        return Link(tt.FullDafnyName, tt.Name) + TypeActualParameters(t.TypeArgs);
      } else if (tt is NewtypeDecl) {
        return Link(tt.FullDafnyName, tt.Name) + TypeActualParameters(t.TypeArgs);
      } else if (tt is DatatypeDecl) {
        return Link(tt.FullDafnyName, tt.Name) + TypeActualParameters(t.TypeArgs);
      } else if (tt is TypeParameter) {
        return tt.Name;
      } else {
        Console.WriteLine("  TYPELINK-B " + t + " " + t.GetType() + " " + tt + " " + tt.GetType());
        return tt.ToString(); // TODO - needs links for every other kind of type
      }
    } else {
      return t.ToString(); // TODO - needs links for every other kind of type
    }
  }

  /** True for declarations that have their own page */
  public bool HasOwnPage(Declaration t) {
    return t is TopLevelDeclWithMembers && (t is ClassDecl || t is TraitDecl || (t as TopLevelDeclWithMembers).Members.Count() > 0);
  }

  /** Fetches the docstring for the given declaration */
  public string Docstring(IHasDocstring d) {
    if (d == null) {
      return null;
    }
    var ds = d.GetDocstring(DafnyProgram.Options);
    if (ds == null) {
      return String.Empty;
    }
    return ds.Trim();
  }

  /** Fetches the abbreviated docstring for the given declaration */
  public string ShortDocstring(IHasDocstring d) {
    var ds = Docstring(d);
    return Shorten(ds);
  }

  /** If there is a docstring, returns a dash + the abbreviated docstring + the more... link */
  public string DashShortDocstring(IHasDocstring d) {
    var docstring = Docstring(d);
    if (!String.IsNullOrEmpty(docstring)) {
      return Mdash + ShortAndMore(d, (d as Declaration).Name);
    }
    return "";
  }

  /** If there is a docstring, returns a dash + the abbreviated docstring (without the more... link) */
  public string DashShortDocstringNoMore(IHasDocstring d) {
    var docstring = ShortDocstring(d);
    if (!String.IsNullOrEmpty(docstring)) {
      return Mdash + docstring;
    }
    return "";
  }

  /** Abbreviates the docstring */
  // Stop at end of sentence (.) but not at periods in numbers or qualified names
  public string Shorten(string docstring) {
    if (docstring != null) {
      var k = docstring.IndexOf(". ");
      if (k == -1) {
        return docstring;
      } else {
        return docstring.Substring(0, k + 1);
      }
    }
    return String.Empty;
  }

  // Used for declarations that merit their own page (modules, classes, traits, types with members, ...)
  public String ShortAndMoreForDecl(TopLevelDecl d) {
    var docstring = Docstring(d as IHasDocstring);
    var shortstring = Shorten(docstring);
    String result = "";
    if (!String.IsNullOrEmpty(docstring)) {
      result = shortstring;
      if (shortstring != docstring) {
        result += " " + LinkToAnchor("decl-detail", "(more...)");
      }
    }
    return result;
  }

  public String ShortAndMore(IHasDocstring d, String target) {
    var docstring = Docstring(d);
    var shortstring = Shorten(docstring);
    String result = "";
    if (!String.IsNullOrEmpty(docstring)) {
      result = shortstring;
      if (shortstring != docstring) {
        result += $" <a href=\"#{target}\">(more...)</a>";
      }
    }
    return result;
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

  public static string QualifiedNameWithLinks(string fullName, bool alsoLast = true) {
    int hash = fullName.IndexOf('#');
    string tail = null;
    if (hash > 0) {
      fullName = fullName.Substring(0, hash);
      tail = fullName.Substring(hash + 1);
    }
    return QualifiedNameWithLinks(fullName, tail, tail, alsoLast);
  }

  public static string QualifiedNameWithLinks(string fullName, string inpage, string text, bool alsoLast = true) {
    var names = fullName.Split('.');
    string nameSoFar = "";
    string output = "";
    int k = names.Length;
    foreach (string name in names) {
      if (names.Length != k) {
        output += ".";
        nameSoFar += ".";
      }
      k--;
      nameSoFar += name;
      output += (k == 0 && !alsoLast ? name : Link(nameSoFar, name));
    }
    if (inpage != null) {
      output += $".<a href=\"{fullName}.html#{inpage}\">{text}</a>";
    }
    return output;
  }

  public static string Link(string fullName, string text) {
    return $"<a href=\"{fullName}.html\">{text}</a>";
  }

  public static string Link(string fullName, string inpage, string text) {
    return $"<a href=\"{fullName}.html#{inpage}\">{text}</a>";
  }

  public string BeginTable() {
    return "<table>";
  }

  public string Row() {
    return $"<tr></tr>";
  }

  public string Row(string s1, string s2) {
    return $"<tr><td>{s1}</td><td>{s2}</td></tr>";
  }

  public string Row(string s1, string s2, string s3) {
    return $"<tr><td>{s1}</td><td>{s2}</td><td>{s3}</td></tr>";
  }

  public string Row(string s1, string s2, string s3, string s4) {
    return $"<tr><td>{s1}</td><td>{s2}</td><td>{s3}</td><td>{s4}</td></tr>";
  }

  public string Row(string s1, string s2, string s3, string s4, string s5) {
    return $"<tr><td>{s1}</td><td>{s2}</td><td>{s3}</td><td>{s4}</td><td>{s5}</td></tr>";
  }

  public string Row(string s1, string s2, string s3, string s4, string s5, string s6) {
    return $"<tr><td>{s1}</td><td>{s2}</td><td>{s3}</td><td>{s4}</td><td>{s5}</td><td>{s6}</td></tr>";
  }

  public String EndTable() {
    return "</table>";
  }

  public String Anchor(string name) {
    return $"<a id=\"{name}\"/>";
  }

  public String LinkToAnchor(string name, string text) {
    return $"<a href=\"#{name}\">{text}</a>";
  }

  public String RuleWithText(String text) {
    return $"<div style=\"width: 100%; height: 10px; border-bottom: 1px solid black; text-align: center\"><span style=\"font-size: 20px; background-color: #F3F5F6; padding: 0 10px;\">{text}</span></div><br>";
  }

  public string Modifiers(MemberDecl d) {
    string result = "";
    if (d.IsGhost) {
      result += "ghost ";
    }
    //if (d.IsAbstract) result += "abstract "; // TODO
    if (d.IsStatic) {
      result += "static ";
    }
    if (d.IsOpaque) {
      result += "opaque ";
    }
    return result;
  }

  public string Attributes(Attributes attribute) {
    if (attribute == null) {
      return "";
    } else {
      string result = Attributes(attribute.Prev) + "{:" + attribute.Name;
      if (attribute.Args == null || attribute.Args.Count() == 0) {
        return result + "}";
      } else {
        var exprs = String.Join(", ", attribute.Args.Select(e => e.ToString()));
        return result + " " + exprs + "}";
      }
    }
  }

  public string TypeFormals(List<TypeParameter> args) {
    return (args.Count == 0) ? "" :
      "&lt;" + String.Join(",", args.Select(a => a.ToString())) + "&gt;";
  }

  public string TypeActualParameters(List<Type> args) {
    return (args.Count == 0) ? "" :
      "&lt;" + String.Join(",", args.Select(a => TypeLink(a))) + "&gt;";
  }

  public void AnnounceFile(string filename) {
    if (Options.CompileVerbose) {
      Console.WriteLine("Writing " + filename);
    }
  }

  /** Adds (a request for) an index entry, with a link to a file */
  public void AddToIndexF(string name, string target, string kind) {
    nameIndex.Add(name + " " + nameIndex.Count + " " + target, kind + " " + QualifiedNameWithLinks(target, null, name));
  }

  /** Adds (a request for) an index entry, with a link to a location in a file */
  public void AddToIndex(string inpageID, string owner, string kind) {
    nameIndex.Add(inpageID + " " + nameIndex.Count + " " + owner, kind + " " + QualifiedNameWithLinks(owner, inpageID, inpageID));
  }

  public void AddToIndexC(string inpageID, string text, string owner, string kind) {
    nameIndex.Add(inpageID + " " + nameIndex.Count + " " + owner, kind + " " + QualifiedNameWithLinks(owner, inpageID, text));
  }

  public void WriteIndex() {
    var keys = nameIndex.Keys.ToList();
    keys.Sort();

    string filename = Outputdir + "/nameindex.html";
    using StreamWriter file = new(filename);
    file.Write(head1);
    file.Write($"Index for program {DafnyProgram.Name}");
    file.Write(head2);
    file.Write(style);
    file.Write($"<div>\n<h1>Index{ProgramHeader()}{space4}{Smaller(contentslink)}</h1>\n</div>");
    file.Write(bodystart);
    foreach (var key in keys) {
      var k = key.IndexOf(' ');
      var value = nameIndex[key]; // Already rewritten as a bunch of links
      var keyn = key.Substring(0, k);
      k = key.LastIndexOf(' ');
      var owner = key.Substring(k + 1);
      var hash = value.IndexOf('#');
      if (hash == -1) {
        file.Write($"<a href=\"{owner}.html\">{keyn}</a>");
      } else if (value.StartsWith("export")) {
        file.Write($"<a href=\"{owner}.html#{ExportSetAnchor(keyn)}\">{keyn}</a>");
      } else {
        var link = value.Substring(0, hash);  // TODO - what is link for // TODO _ do export sets work
        file.Write($"<a href=\"{owner}.html#{keyn}\">{keyn}</a>");
      }
      file.WriteLine(Mdash + value + br);
    }
    file.Write(foot);
    AnnounceFile(filename);
  }

  public static string DefaultOutputDir = "./docs";

  static string eol = "\n";

  static string br = "<br>";

  public static string Mdash = " &mdash; ";

  static string contentslink = "<a href=\"index.html\">[table of contents]</a>";
  static string indexlink = "<a href=\"nameindex.html\">[index]</a>";


  static string space4 = "&nbsp;&nbsp;&nbsp;&nbsp;";

  static string indent = "<p style=\"margin-left: 25px;\">";
  static string unindent = "</p>";
  static string head1 =
  @"<!doctype html>

<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">

  <title>";

  static string head2 =
  @"</title>
  <link rel=""icon"" type=""image/png"" href=""dafny-favicon.png"">
  <meta name=""description"" content=""Documentation for Dafny code produced by dafnydoc"">
  <meta name=""author"" content=""dafnydoc"">
</head>
";

  static string style =
  @"<style>
body {
  background-color: white;
}

h1 {
  color: blue;
  text-align: center;
  background-color: #fceb6c
}
h2 {
  color: blue;
  text-align: left;
  background-color: #fceb6c
}
h3 {
  color: blue;
  text-align: left;
  background-color: #fefdcc
}

p {
  font-size: 16px
}
</style>
";

  static string bodystart =
  @"<body>
";

  public static string foot =
  @"</body>
</html>
";

}

public static partial class Extensions {
  /// <summary>
  ///     A string extension method that replace first occurence.
  /// </summary>
  /// <param name="this">The @this to act on.</param>
  /// <param name="oldValue">The old value.</param>
  /// <param name="newValue">The new value.</param>
  /// <returns>The string with the first occurence of old value replace by new value.</returns>
  public static string ReplaceFirst(this string @this, string oldValue, string newValue) {
    int startindex = @this.IndexOf(oldValue);

    if (startindex == -1) {
      return @this;
    }

    return @this.Remove(startindex, oldValue.Length).Insert(startindex, newValue);
  }

  /// <summary>
  ///     A string extension method that replace first number of occurences.
  /// </summary>
  /// <param name="this">The @this to act on.</param>
  /// <param name="number">Number of.</param>
  /// <param name="oldValue">The old value.</param>
  /// <param name="newValue">The new value.</param>
  /// <returns>The string with the numbers of occurences of old value replace by new value.</returns>
  public static string ReplaceFirst(this string @this, int number, string oldValue, string newValue) {
    List<string> list = @this.Split(oldValue).ToList();
    int old = number + 1;
    IEnumerable<string> listStart = list.Take(old);
    IEnumerable<string> listEnd = list.Skip(old);

    return string.Join(newValue, listStart) +
           (listEnd.Any() ? oldValue : "") +
           string.Join(oldValue, listEnd);
  }
}

