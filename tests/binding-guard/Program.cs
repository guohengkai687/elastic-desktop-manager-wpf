using System.Text.RegularExpressions;
using System.Xml.Linq;

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

// XAML 文本 → 元素序列。解析失败时返回空并输出原因；
// 调用方必须把 parseError 当失败上报，否则规则会因"文件没检查"而假绿。
static List<XElement> SafeDescendants(string xaml, out string? parseError)
{
    parseError = null;
    try { return XDocument.Parse(xaml).Descendants().ToList(); }
    catch (Exception ex)
    {
        parseError = $"XAML 解析失败，规则未能检查该文件：{ex.Message}";
        return new List<XElement>();
    }
}

// 属性值是否为"单个合法的标记扩展"（允许嵌套，如 {Binding X, Converter={StaticResource C}}）。
// 返回 false 表示值里混有字面量文本，此时 XAML 不会解析内嵌的 {StaticResource}。
static bool IsSingleMarkupExtension(string value)
{
    string v = value.Trim();
    if (!v.StartsWith("{", StringComparison.Ordinal)) return false;
    if (!v.EndsWith("}", StringComparison.Ordinal)) return false;
    int depth = 0;
    for (int i = 0; i < v.Length; i++)
    {
        if (v[i] == '{') depth++;
        else if (v[i] == '}')
        {
            depth--;
            if (depth == 0) return i == v.Length - 1; // 最外层闭合必须落在末尾
        }
    }
    return false;
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
//         XAML 的 StaticResource 缺失 → 加载时抛 XamlParseException 直接崩溃；
//         C# 的 FindResource 缺失 → 抛 ResourceReferenceKeyNotFoundException 直接崩溃。
// 注意：StaticResource 与 DynamicResource 都必须查。曾只查了 DynamicResource，
//       于是 {StaticResource 拼错的 key} 这类崩溃没有任何规则能拦住（真实漏洞）。
// 前置条件：所有资源都集中在 Themes/*.xaml 与 App.xaml，Views 不定义本地资源，
//           因此"key 必须出现在全局定义集中"不会误报（已核对，Views 中无 ResourceDictionary）。
Check("资源：XAML/C# 引用的资源 key 均已定义（含 StaticResource）", () =>
{
    var defined = new HashSet<string>(StringComparer.Ordinal);
    foreach (var f in Directory.GetFiles(Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes"), "*.xaml")
                 .Concat(new[] { Path.Combine(repoRoot, "src", "ElasticDesktopManager", "App.xaml") }))
        defined.UnionWith(ResourceKeys(File.ReadAllText(f)));

    var missing = new List<string>();

    // XAML: {DynamicResource Key} / {StaticResource Key} / <StaticResource ResourceKey="Key" />
    foreach (var f in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories)
                 .Concat(Directory.GetFiles(Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes"), "*.xaml"))
                 .Concat(new[] { Path.Combine(repoRoot, "src", "ElasticDesktopManager", "MainWindow.xaml") }))
    {
        string rel = Path.GetRelativePath(repoRoot, f);
        string text = File.ReadAllText(f);

        // 只匹配 "{XxxResource 标识符}"，不会误吃 BasedOn="{StaticResource {x:Type Button}}" 这种类型引用
        foreach (Match m in Regex.Matches(text, @"\{(Dynamic|Static)Resource\s+([A-Za-z_]\w*)\s*\}"))
        {
            if (!defined.Contains(m.Groups[2].Value))
                missing.Add($"{rel}: {{{m.Groups[1].Value} {m.Groups[2].Value}}} 未定义");
        }

        foreach (Match m in Regex.Matches(text, @"<StaticResource\s+ResourceKey=""([^""]+)"""))
        {
            if (!defined.Contains(m.Groups[1].Value))
                missing.Add($"{rel}: <StaticResource ResourceKey=\"{m.Groups[1].Value}\" /> 未定义");
        }

        // 写法错误：资源引用被嵌在更长的字符串里（如 Margin="0,{StaticResource Space2},0,0"）。
        // XAML 不解析内嵌的标记扩展，会整体当字面量文本 → 目标属性转换失败 → 运行期崩溃。
        // 非均匀边距请单独定义 Thickness 令牌（如 <Thickness x:Key="Gap0_0_0_8">0,0,0,8</Thickness>）。
        var elements = SafeDescendants(text, out string? parseError);
        if (parseError is not null)
        {
            missing.Add($"{rel}: {parseError}");
            continue;
        }

        foreach (var el in elements)
        {
            foreach (var attr in el.Attributes())
            {
                string v = attr.Value;
                if (!v.Contains("{StaticResource", StringComparison.Ordinal)) continue;
                if (v.TrimStart().StartsWith("{}", StringComparison.Ordinal)) continue; // 显式转义为字面量，尊重作者意图
                if (IsSingleMarkupExtension(v)) continue;                              // 合法：单个标记扩展（可嵌套）
                missing.Add($"{rel}: <{el.Name.LocalName} {attr.Name.LocalName}=\"{v}\"> 资源引用被嵌在字符串中 → XAML 视为字面量 → 运行期转换失败");
            }
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

// ============================================================
// 规则：设计令牌的声明类型必须与目标属性类型精确匹配
//
// 背景（真实线上崩溃，非假设）：Tokens.xaml 曾把 TitleBarHeight 声明为 sys:Double，
// 而 RowDefinition.Height 的类型是 GridLength，其 GridLengthConverter 只接受字符串，
// 于是窗口加载即抛 XamlParseException：「"52"不是属性"Height"的有效值。」
// 编译期 0 错误 0 警告，只有真机运行才炸。同类陷阱：
//   sys:Double → Padding/Margin/BorderThickness(Thickness)、CornerRadius、Duration
// 判定：令牌类型 == 目标属性类型；或令牌是 string（由目标属性自带的转换器处理，任意类型都安全）。
// 保守原则：属性不在下表内 → 不检查（宁可漏报，不可误报；误报会让守卫被无视）。
// ============================================================

// 目标属性 → 期望的令牌类型；null 表示"未知，不检查"
static string? ExpectedTokenType(string ownerType, string property)
{
    switch (ownerType + "." + property)
    {
        // 具体元素优先：网格轨道是 GridLength，而普通 Height/Width 是 double
        case "RowDefinition.Height":
        case "RowDefinition.MinHeight":
        case "RowDefinition.MaxHeight":
        case "ColumnDefinition.Width":
        case "ColumnDefinition.MinWidth":
        case "ColumnDefinition.MaxWidth":
            return "GridLength";
    }

    switch (property)
    {
        case "Margin":
        case "Padding":
        case "BorderThickness":
            return "Thickness";
        case "CornerRadius":
            return "CornerRadius";
        case "FontSize":
        case "Width":
        case "Height":
        case "MinWidth":
        case "MinHeight":
        case "MaxWidth":
        case "MaxHeight":
        case "Opacity":
            return "Double";
        case "FontFamily":
            return "FontFamily";
        case "Duration":
            return "Duration";
        case "Effect":
            return "Effect";
        default:
            return null;
    }
}

static bool TokenTypeUsableFor(string tokenType, string expectedType)
{
    if (tokenType == expectedType) return true;
    if (tokenType == "String") return true; // 字符串走目标属性自身的 TypeConverter
    if (expectedType == "Effect" && tokenType.EndsWith("Effect", StringComparison.Ordinal)) return true;
    if (expectedType == "Brush" && tokenType.EndsWith("Brush", StringComparison.Ordinal)) return true;
    return false;
}

// 解析 Setter 所属 Style/ControlTemplate 的 TargetType → 简单类型名
static string TargetTypeOf(XElement el)
{
    var host = el.Ancestors().FirstOrDefault(a => a.Name.LocalName is "Style" or "ControlTemplate" or "DataTemplate");
    string raw = ((string?)host?.Attribute("TargetType") ?? "*").Trim();
    if (raw.StartsWith("{x:Type", StringComparison.Ordinal))
        raw = raw.Trim('{', '}').Split(' ').Last().Trim();
    if (raw.Contains(':')) raw = raw.Split(':').Last().Trim();
    return raw.Length == 0 ? "*" : raw;
}

static List<string> FindTokenTypeMismatches(string xaml, string label, Dictionary<string, string> tokenTypes)
{
    var hits = new List<string>();
    XDocument doc;
    try { doc = XDocument.Parse(xaml); }
    catch (Exception ex)
    {
        // 不能静默跳过：静默 = 规则失效 = 假绿
        hits.Add($"{label}: XAML 解析失败，令牌类型规则未能检查该文件（{ex.Message}）");
        return hits;
    }

    foreach (var el in doc.Descendants())
    {
        foreach (var attr in el.Attributes())
        {
            var m = Regex.Match(attr.Value.Trim(), @"^\{StaticResource\s+([A-Za-z_]\w*)\s*\}$");
            if (!m.Success) continue;
            string key = m.Groups[1].Value;
            if (!tokenTypes.TryGetValue(key, out string? tokenType)) continue; // 非设计令牌（如 Style 资源）→ 不管

            string prop = attr.Name.LocalName;
            string owner = el.Name.LocalName;
            if (owner == "Setter")
            {
                if (prop != "Value") continue;
                string? p = (string?)el.Attribute("Property");
                if (p is null) continue;
                if (p.Contains('.')) { owner = p.Split('.')[0]; prop = p.Split('.').Last(); }
                else { owner = TargetTypeOf(el); prop = p; }
            }

            string? expected = ExpectedTokenType(owner, prop);
            if (expected is null) continue;

            if (!TokenTypeUsableFor(tokenType, expected))
                hits.Add($"{label}: <{owner} {prop}=\"{{StaticResource {key}}}\"> → 令牌 {key} 声明为 {tokenType}，该属性需要 {expected} → 运行期转换失败（XamlParseException）");
        }
    }
    return hits;
}

static Dictionary<string, string> TokenTypeMap(string tokensPath)
{
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    var xns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
    foreach (var el in XDocument.Load(tokensPath).Descendants())
    {
        var key = (string?)el.Attribute(xns + "Key");
        if (key is not null) map[key] = el.Name.LocalName;
    }
    return map;
}

// ---- 规则：令牌类型必须匹配（Double 用于 GridLength/Thickness 会崩）----
Check("令牌：设计令牌的声明类型与目标属性类型匹配", () =>
{
    var tokenTypes = TokenTypeMap(Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes", "Tokens.xaml"));
    if (tokenTypes.Count == 0) throw new Exception("Tokens.xaml 未解析到任何令牌 —— 守卫失效");

    var hits = new List<string>();
    foreach (var f in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories)
                 .Concat(Directory.GetFiles(Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes"), "*.xaml"))
                 .Concat(new[]
                 {
                     Path.Combine(repoRoot, "src", "ElasticDesktopManager", "MainWindow.xaml"),
                     Path.Combine(repoRoot, "src", "ElasticDesktopManager", "App.xaml"),
                 }))
    {
        hits.AddRange(FindTokenTypeMismatches(File.ReadAllText(f), Path.GetRelativePath(repoRoot, f), tokenTypes));
    }

    if (hits.Count > 0)
        throw new Exception($"发现 {hits.Count} 处令牌类型不匹配：\n    " + string.Join("\n    ", hits.Distinct()));
});

Check("守卫自检：令牌类型不匹配必须能被抓出（且不误报）", () =>
{
    var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BadDouble"] = "Double",       // 真实崩溃的形态
        ["GoodHeight"] = "GridLength",
        ["StrToken"] = "String",
        ["Space"] = "Thickness",
    };

    // 必须失败：Double → RowDefinition.Height（复现线上崩溃）
    string badHeight = """<Grid><Grid.RowDefinitions><RowDefinition Height="{StaticResource BadDouble}" /></Grid.RowDefinitions></Grid>""";
    if (FindTokenTypeMismatches(badHeight, "t", tokens).Count != 1)
        throw new Exception("未能抓出 Double → RowDefinition.Height（假绿：守卫已失效）");

    // 必须通过：GridLength → RowDefinition.Height（精确类型）
    string okHeight = """<Grid><Grid.RowDefinitions><RowDefinition Height="{StaticResource GoodHeight}" /></Grid.RowDefinitions></Grid>""";
    if (FindTokenTypeMismatches(okHeight, "t", tokens).Count != 0)
        throw new Exception("误报：GridLength → RowDefinition.Height 应为合法");

    // 必须通过：string 令牌由目标属性转换器处理
    string strHeight = """<Grid><Grid.RowDefinitions><RowDefinition Height="{StaticResource StrToken}" /></Grid.RowDefinitions></Grid>""";
    if (FindTokenTypeMismatches(strHeight, "t", tokens).Count != 0)
        throw new Exception("误报：string 令牌应放行");

    // 必须失败：Double → Boxing Thickness（第二形态）
    if (FindTokenTypeMismatches("""<Border Padding="{StaticResource BadDouble}" />""", "t", tokens).Count != 1)
        throw new Exception("未能抓出 Double → Padding(Thickness)");

    // 必须通过：Thickness → Padding
    if (FindTokenTypeMismatches("""<Border Padding="{StaticResource Space}" />""", "t", tokens).Count != 0)
        throw new Exception("误报：Thickness → Padding 应为合法");

    // 必须通过：非设计令牌（Style/模板资源）不受本规则约束
    if (FindTokenTypeMismatches("""<Button Style="{StaticResource SomeStyle}" />""", "t", tokens).Count != 0)
        throw new Exception("误报：非令牌资源不应被检查");

    // 必须通过：Setter 解析 TargetType（Border.Padding = Thickness）
    string setterOk = """<Style TargetType="Border"><Setter Property="Padding" Value="{StaticResource Space}" /></Style>""";
    if (FindTokenTypeMismatches(setterOk, "t", tokens).Count != 0)
        throw new Exception("误报：Setter 中 Thickness → Border.Padding 应为合法");

    // 必须失败：Setter 中 Double → Border.Padding
    string setterBad = """<Style TargetType="Border"><Setter Property="Padding" Value="{StaticResource BadDouble}" /></Style>""";
    if (FindTokenTypeMismatches(setterBad, "t", tokens).Count != 1)
        throw new Exception("未能抓出 Setter 中的 Double → Padding");

    // 必须失败：Setter 中 Double → ColumnDefinition.Width（TargetType 解析路径）
    string setterCol = """<Style TargetType="ColumnDefinition"><Setter Property="Width" Value="{StaticResource BadDouble}" /></Style>""";
    if (FindTokenTypeMismatches(setterCol, "t", tokens).Count != 1)
        throw new Exception("未能抓出 Setter 中的 Double → ColumnDefinition.Width");

    // 必须失败：XAML 解析失败不能被静默吞掉（否则规则会假绿）
    if (FindTokenTypeMismatches("<a><b></a>", "t", tokens).Count != 1)
        throw new Exception("XAML 解析失败被静默跳过 —— 规则可能假绿");
});

// ---- 守卫自检：确保上面四条规则真的能失败（假绿比没有守卫更危险）----
Check("守卫自检：主题 key 差异 / 硬编码颜色 / 未定义 key / 内嵌资源引用 均能被识别", () =>
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

    // StaticResource 缺 key 必须能被抓到（此前规则只查 DynamicResource，是真实漏洞：
    // StaticResource 缺 key 会在加载时抛 XamlParseException 直接崩溃）
    if (Regex.Matches("""<Border Background="{StaticResource Missing}" />""", @"\{(Dynamic|Static)Resource\s+([A-Za-z_]\w*)\s*\}").Count != 1)
        throw new Exception("未匹配到 {StaticResource …} —— 资源规则已失效");
    if (Regex.Matches("""<StaticResource ResourceKey="Missing" />""", @"<StaticResource\s+ResourceKey=""([^""]+)""").Count != 1)
        throw new Exception("未匹配到 <StaticResource ResourceKey=… /> —— 资源规则已失效");
    // 类型引用不应被当成 key（否则 BasedOn="{StaticResource {x:Type Button}}" 会误报）
    if (Regex.Matches("""BasedOn="{StaticResource {x:Type Button}}" """, @"\{(Dynamic|Static)Resource\s+([A-Za-z_]\w*)\s*\}").Count != 0)
        throw new Exception("误报：{x:Type …} 类型引用不应被当作资源 key");

    // 内嵌资源引用（XAML 当字面量 → 运行期转换失败）必须能被识别
    if (IsSingleMarkupExtension("0,{StaticResource Space2},0,0"))
        throw new Exception("误判：内嵌资源不应算合法标记扩展");
    if (IsSingleMarkupExtension("{StaticResource A} {StaticResource B}"))
        throw new Exception("误判：两个标记扩展拼接不应算合法");
    if (!IsSingleMarkupExtension("{Binding X, Converter={StaticResource C}}"))
        throw new Exception("误判：嵌套标记扩展应算合法");

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
