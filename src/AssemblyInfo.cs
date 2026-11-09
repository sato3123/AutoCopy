using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: AssemblyTitle("自動コピー")]
[assembly: AssemblyProduct("自動コピー")]
[assembly: AssemblyDescription("フォルダの差分コピーを自動で行う常駐アプリ")]
[assembly: AssemblyCompany("自動コピー")]
[assembly: AssemblyCopyright("自動コピー")]
// 版：新しい版を配るときは必ず上げる（自動更新は、置き場所の版が新しいときだけ行う）
#if UPDATEFAIL
[assembly: AssemblyVersion("100.0.0.0")]      // 自動更新のテスト用（起動できない版）
[assembly: AssemblyFileVersion("100.0.0.0")]
#elif UPDATETEST
[assembly: AssemblyVersion("99.0.0.0")]       // 自動更新のテスト用
[assembly: AssemblyFileVersion("99.0.0.0")]
#else
[assembly: AssemblyVersion("1.16.6.0")]
[assembly: AssemblyFileVersion("1.16.6.0")]
#endif
[assembly: ComVisible(false)]
// .NET Framework 4.8 向けとして動かす（新しいパス処理などの既定値を使うため）
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
