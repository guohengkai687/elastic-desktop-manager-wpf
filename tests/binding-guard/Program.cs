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
//
// 注意：这里是**类型盲**的名字查找（不解析绑定的 DataContext 类型），
// 因此同名属性可能出现在多个 VM 中（如 QueryCondition.Value 可写、
// 而某个展示模型也有 Value 且只读）。为**避免误报**（误报会让守卫被无视，比没有守卫更糟），
// 采用保守规则：只有该名字在**所有**声明处都只读时才判定为只读；
// 只要存在一个可写声明就不报。
static bool IsReadOnlyProperty(string vmSource, string propName)
{
    var matches = Regex.Matches(vmSource, PropertyPattern(propName), RegexOptions.Multiline);
    if (matches.Count == 0) return false; // 找不到（可能来自基类/其它文件）→ 不判为只读，避免误报
    return matches.All(m => IsReadOnlyMatch(vmSource, m));
}

// 支持 public / public required / public static / public override 等修饰符组合
static string PropertyPattern(string propName)
    => $@"public\s+(?:required\s+|static\s+|override\s+|virtual\s+|sealed\s+|new\s+)*[\w\?<>\[\],\.]+\s+{Regex.Escape(propName)}\s*(?:\{{|=>)";

static bool IsReadOnlyMatch(string vmSource, Match m)
{
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

    // 回归 1：带 required 修饰符的只读属性也必须能识别（旧正则漏掉 public required string X）
    const string requiredSample = """
        public required string R { get; init; }
    """;
    if (!IsReadOnlyProperty(requiredSample, "R"))
        throw new Exception("未识别 public required ... { get; init; } 为只读");

    // 回归 2：同名属性同时存在“可写”和“只读”声明时，不得误报为只读。
    // 这是真实踩过的坑：QueryCondition.Value 可写，而某个展示模型也有只读的 Value，
    // 类型盲查找会把 SearchView 里合法的 TwoWay 绑定误判成错误。
    const string collisionSample = """
        public class A { public string Value { get => _v; set => SetProperty(ref _v, value); } }
        public class B { public string Value { get; init; } }
    """;
    if (IsReadOnlyProperty(collisionSample, "Value"))
        throw new Exception("同名属性存在可写声明时仍误报为只读（类型盲误报回归）");

    // 回归 3：唯一且只读的属性仍必须被抓出来（避免上面的保守规则把守卫改废）
    const string onlyReadOnly = """
        public class C { public string ResponseText { get => _r; private set => SetProperty(ref _r, value); } }
    """;
    if (!IsReadOnlyProperty(onlyReadOnly, "ResponseText"))
        throw new Exception("唯一只读属性未被识别——保守规则削弱了守卫");
});

// ---- i18n 中英文词条一致性 ----
Check("i18n：zh_CN 与 en 词条完全对齐（无单边缺失）", () =>
{
    string locFile = Path.Combine(repoRoot, "src", "ElasticDesktopManager.Core", "I18n", "Localization.cs");
    string src = File.ReadAllText(locFile);
    int zhStart = src.IndexOf("Dictionary<string, string> Zh", StringComparison.Ordinal);
    int enStart = src.IndexOf("Dictionary<string, string> En", StringComparison.Ordinal);
    if (zhStart < 0 || enStart < 0 || enStart < zhStart)
        throw new Exception("未能定位 Zh / En 词典声明");

    static HashSet<string> Keys(string block) =>
        Regex.Matches(block, @"\[""([^""]+)""\]\s*=").Select(m => m.Groups[1].Value).ToHashSet();

    var zh = Keys(src[zhStart..enStart]);
    var en = Keys(src[enStart..]);
    var onlyZh = zh.Except(en).OrderBy(x => x).ToList();
    var onlyEn = en.Except(zh).OrderBy(x => x).ToList();
    if (onlyZh.Count > 0 || onlyEn.Count > 0)
        throw new Exception($"仅中文有: [{string.Join(", ", onlyZh)}]；仅英文有: [{string.Join(", ", onlyEn)}]");
});


// ============================================================
// 通用工具：资源 key 收集 / 硬编码颜色检测
// 这两类问题同样「编译零错误、运行期才炸或静默不可见」，与只读绑定同源。
// ============================================================

static HashSet<string> ResourceKeys(string xaml) =>
    Regex.Matches(xaml, @"x:Key=""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();

static List<string> FindHardcodedColors(string xaml, string label)
{
    var hits = new List<string>();
    foreach (Match m in Regex.Matches(xaml, @"""#[0-9A-Fa-f]{3,8}"""))
        hits.Add($"{label}: {m.Value}");
    return hits;
}

// ---- 规则：Dark / Light 主题 key 必须完全一致 ----
Check("主题：Dark 与 Light 的颜色 key 完全一致（缺一边 → 该主题下元素静默不可见）", () =>
{
    var dark = ResourceKeys(File.ReadAllText(Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes", "Dark.xaml")));
    var light = ResourceKeys(File.ReadAllText(Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes", "Light.xaml")));
    var onlyDark = dark.Except(light).OrderBy(x => x).ToList();
    var onlyLight = light.Except(dark).OrderBy(x => x).ToList();
    if (onlyDark.Count > 0 || onlyLight.Count > 0)
        throw new Exception($"仅 Dark 有: [{string.Join(", ", onlyDark)}]；仅 Light 有: [{string.Join(", ", onlyLight)}]");
    if (dark.Count == 0) throw new Exception("Dark/Light 未解析到任何 key —— 守卫失效");
});

// ---- 规则：XAML 不得硬编码颜色（必须走 DynamicResource，否则主题切换失效）----
Check("XAML：不存在硬编码 #RRGGBB 颜色（应为 DynamicResource，保证主题切换）", () =>
{
    var hits = new List<string>();
    foreach (var f in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories)
                 .Concat(new[] { Path.Combine(repoRoot, "src", "ElasticDesktopManager", "MainWindow.xaml") }))
    {
        hits.AddRange(FindHardcodedColors(File.ReadAllText(f), Path.GetRelativePath(repoRoot, f)));
    }
    if (hits.Count > 0)
        throw new Exception($"发现 {hits.Count} 处硬编码颜色：\n    " + string.Join("\n    ", hits.Distinct()));
});

// ---- 规则：所有被引用的资源 key 必须存在 ----
// 运行期：XAML 的 DynamicResource 缺失 → 元素静默不可见；
//         C# 的 FindResource 缺失 → 抛 ResourceReferenceKeyNotFoundException 直接崩溃。
Check("资源：XAML/C# 引用的资源 key 均已定义", () =>
{
    var defined = new HashSet<string>(StringComparer.Ordinal);
    foreach (var f in Directory.GetFiles(Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes"), "*.xaml")
                 .Concat(new[] { Path.Combine(repoRoot, "src", "ElasticDesktopManager", "App.xaml") }))
        defined.UnionWith(ResourceKeys(File.ReadAllText(f)));

    var missing = new List<string>();

    // XAML: {DynamicResource Key}
    foreach (var f in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories)
                 .Concat(Directory.GetFiles(Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes"), "*.xaml"))
                 .Concat(new[] { Path.Combine(repoRoot, "src", "ElasticDesktopManager", "MainWindow.xaml") }))
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{DynamicResource\s+([A-Za-z_]\w*)\s*\}"))
        {
            if (!defined.Contains(m.Groups[1].Value))
                missing.Add($"{Path.GetRelativePath(repoRoot, f)}: {{DynamicResource {m.Groups[1].Value}}} 未定义");
        }
    }

    // C#: FindResource("Key") —— TryFindResource 有回退，只需检查字面量 FindResource
    foreach (var f in Directory.GetFiles(Path.Combine(repoRoot, "src", "ElasticDesktopManager"), "*.cs", SearchOption.AllDirectories))
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(f), @"[^y]FindResource\(\s*""([^""]+)""\s*\)"))
        {
            if (!defined.Contains(m.Groups[1].Value))
                missing.Add($"{Path.GetRelativePath(repoRoot, f)}: FindResource(\"{m.Groups[1].Value}\") 未定义 → 运行期抛异常");
        }
    }

    if (missing.Count > 0)
        throw new Exception($"发现 {missing.Count} 处未定义引用：\n    " + string.Join("\n    ", missing.Distinct()));
});

// ---- 守卫自检：确保上面三条规则真的能失败（假绿比没有守卫更危险）----
Check("守卫自检：主题 key 差异 / 硬编码颜色 / 未定义 key 均能被识别", () =>
{
    // 主题 key 差异
    var a = ResourceKeys("""<SolidColorBrush x:Key="OnlyA" />""");
    var b = ResourceKeys("""<SolidColorBrush x:Key="OnlyB" />""");
    if (a.Except(b).Count() != 1) throw new Exception("未能识别 Dark 独有 key");

    // 硬编码颜色：三种写法都要抓到
    var colors = FindHardcodedColors("""<Border Background="#CC333333" BorderBrush="#FFF" />""", "t");
    if (colors.Count != 2) throw new Exception($"应抓到 2 处硬编码颜色，实际 {colors.Count}");
    if (FindHardcodedColors("""<Border Background="{DynamicResource SurfaceBrush}" />""", "t").Count != 0)
        throw new Exception("误报：DynamicResource 不应算硬编码");

    // 未定义 key 识别
    var defined = new HashSet<string> { "SurfaceBrush" };
    if (defined.Contains("TextBrush")) throw new Exception("自检逻辑有误");

    // i18n 计数：单边缺失要被发现
    static HashSet<string> K(string block) =>
        Regex.Matches(block, @"\[""([^""]+)""\]\s*=").Select(m => m.Groups[1].Value).ToHashSet();
    var zh = K("""["a"]="1",["b"]="2",""");
    var en = K("""["a"]="1",""");
    if (zh.Count == en.Count) throw new Exception("未能识别 zh/en 词条数不等");
});

// ---- 规则：zh 与 en 词条数量必须相等（不只看单边缺失，也防重复 key 掩盖）----
Check("i18n：zh_CN 与 en 词条数量完全相等", () =>
{
    string locFile = Path.Combine(repoRoot, "src", "ElasticDesktopManager.Core", "I18n", "Localization.cs");
    string src = File.ReadAllText(locFile);
    int zhStart = src.IndexOf("Dictionary<string, string> Zh", StringComparison.Ordinal);
    int enStart = src.IndexOf("Dictionary<string, string> En", StringComparison.Ordinal);
    if (zhStart < 0 || enStart < 0 || enStart < zhStart)
        throw new Exception("未能定位 Zh / En 词典声明");

    static HashSet<string> Keys(string block) =>
        Regex.Matches(block, @"\[""([^""]+)""\]\s*=").Select(m => m.Groups[1].Value).ToHashSet();

    var zh = Keys(src[zhStart..enStart]);
    var en = Keys(src[enStart..]);
    if (zh.Count != en.Count)
        throw new Exception($"zh_CN {zh.Count} 条 vs en {en.Count} 条（相差 {Math.Abs(zh.Count - en.Count)}）");
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
