using System.Text.RegularExpressions;

// ============================================================
// binding-guard — XAML 绑定契约静态检查（Linux 可跑）
//
// 背景：WPF 的 TextBox.Text / CheckBox.IsChecked / ComboBox.SelectedItem 等
// 目标属性默认是 TwoWay 绑定。若源属性只有 getter（或 private set），
// 运行时会抛 InvalidOperationException（“无法对只读属性 X 进行 TwoWay 绑定”），
// 而编译期完全不会报错——本项目已因此踩过 ResponseText / RawJson 等 7 处坑。
// 该守卫在 CI/本地构建时把这些错误提前抓出来。
// ============================================================

int passed = 0, failed = 0;
var failures = new List<string>();

void Check(string name, Action fn)
{
    try { fn(); passed++; Console.WriteLine($"  PASS  {name}"); }
    catch (Exception ex) { failed++; failures.Add($"{name}: {ex.Message}"); Console.WriteLine($"  FAIL  {name} => {ex.Message}"); }
}

string repoRoot = FindRepoRoot();
string viewsDir = Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Views");
string vmDir = Path.Combine(repoRoot, "src", "ElasticDesktopManager", "ViewModels");

if (!Directory.Exists(viewsDir))
    throw new Exception($"找不到 Views 目录：{viewsDir}");

// 默认 TwoWay 的 (控件, 属性) 组合 —— 这些目标的绑定源必须是可写属性
var twoWayDefaults = new (string Control, string Prop)[]
{
    ("TextBox", "Text"),
    ("CheckBox", "IsChecked"),
    ("ComboBox", "SelectedItem"),
    ("ComboBox", "SelectedValue"),
    ("ComboBox", "SelectedIndex"),
    ("ListBox", "SelectedItem"),
    ("PasswordBox", "Password"),
};

// 源属性只读的判定：源码中该属性只有 get、没有 public set
static bool IsReadOnlyProperty(string vmSource, string propName)
{
    var m = Regex.Match(vmSource,
        $@"public\s+[\w\?<>\[\],\.]+\s+{Regex.Escape(propName)}\s*(?:\{{|=>)",
        RegexOptions.Multiline);
    if (!m.Success) return false; // 找不到（可能来自基类/其它文件）→ 不判为只读，避免误报

    // 先判定属性形态，再看修饰符，避免把下一个属性的花括号当成自己的
    bool isBlockBody = m.Value.TrimEnd().EndsWith("{");
    if (!isBlockBody)
        return true; // 表达式体属性 "public string X => ..."：只读

    // 关键：花括号位置必须取自“匹配片段自身”。
    // 用 vmSource.IndexOf('{', m.Index) 会命中前一个属性遗留的 {，导致属性体错位。
    int braceStart = m.Index + m.Value.LastIndexOf('{');

    int depth = 0, i = braceStart;
    for (; i < vmSource.Length; i++)
    {
        if (vmSource[i] == '{') depth++;
        else if (vmSource[i] == '}')
        {
            depth--;
            if (depth == 0) break;
        }
    }
    string body = vmSource[braceStart..Math.Min(i + 1, vmSource.Length)];

    if (Regex.IsMatch(body, @"(?:private|protected|internal)\s+set\b")) return true; // private set：外部不可写
    // 有 get 且没有任何 set（set; / set { / set =>）才算只读
    return Regex.IsMatch(body, @"\bget\b") && !Regex.IsMatch(body, @"\bset\b");
}

// 汇总所有 VM 源码，供跨文件属性查找
string allVmSource = string.Join("\n", Directory.GetFiles(vmDir, "*.cs", SearchOption.AllDirectories)
    .Select(File.ReadAllText));

var violations = new List<string>();

foreach (var xaml in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories))
{
    string text = File.ReadAllText(xaml);
    string rel = Path.GetRelativePath(repoRoot, xaml);

    foreach (var (control, prop) in twoWayDefaults)
    {
        // 逐个抓取 <Control ... Prop="{Binding ...}" ...>，允许属性跨行
        foreach (Match tag in Regex.Matches(text, $@"<{control}\b[^>]*?>", RegexOptions.Singleline))
        {
            var bind = Regex.Match(tag.Value, $@"\b{prop}\s*=\s*""\{{Binding\s+([^""]+)}}""");
            if (!bind.Success) continue;

            string expr = bind.Groups[1].Value;
            if (expr.Contains("Mode=OneWay") || expr.Contains("Mode=OneTime")) continue; // 显式单向：安全
            if (expr.Contains("Mode=TwoWay")) { /* 显式双向：仍需可写 */ }

            // 源属性名 = 表达式首个标识符
            var nameMatch = Regex.Match(expr, @"^\s*([A-Za-z_][A-Za-z0-9_]*)");
            if (!nameMatch.Success) continue;
            string sourceProp = nameMatch.Groups[1].Value;

            if (IsReadOnlyProperty(allVmSource, sourceProp))
                violations.Add($"{rel}: <{control} {prop}=\"{{Binding {sourceProp}}}\"> 但 {sourceProp} 只读 → 运行时会抛异常，请加 Mode=OneWay");
        }
    }
}

Check("XAML 中不存在“只读属性 + 默认 TwoWay 目标”的绑定", () =>
{
    if (violations.Count > 0)
        throw new Exception("发现 " + violations.Count + " 处：\n    " + string.Join("\n    ", violations.Distinct()));
});

Check("守卫自身有效性：能识别 private set 与表达式体属性为只读", () =>
{
    const string sample = """
        public string A { get => _a; private set => SetProperty(ref _a, value); }
        public string B => "x";
        public string C { get; set; }
        public string D
        {
            get => _d;
            private set => SetProperty(ref _d, value);
        }
    """;
    if (!IsReadOnlyProperty(sample, "A")) throw new Exception("未识别 private set 为只读");
    if (!IsReadOnlyProperty(sample, "B")) throw new Exception("未识别表达式体属性为只读");
    if (IsReadOnlyProperty(sample, "C")) throw new Exception("误判可写属性 C 为只读");
    if (!IsReadOnlyProperty(sample, "D")) throw new Exception("未识别多行 private set 为只读");
});


Console.WriteLine();
Console.WriteLine($"===== 结果：通过 {passed}，失败 {failed} =====");
if (failed > 0)
{
    Console.WriteLine("失败明细：");
    foreach (var f in failures) Console.WriteLine("  - " + f);
}
return failed == 0 ? 0 : 1;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ElasticDesktopManager.sln")))
            return dir.FullName;
        dir = dir.Parent!;
    }
    throw new Exception("向上未找到 ElasticDesktopManager.sln");
}
