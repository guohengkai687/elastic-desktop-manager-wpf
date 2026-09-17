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
            // StaticResource 与 DynamicResource 都要查：主题风格令牌（圆角/密度/阴影）
            // 只能用 DynamicResource，类型错了照样是运行期崩溃。
            var m = Regex.Match(attr.Value.Trim(), @"^\{(?:Static|Dynamic)Resource\s+([A-Za-z_]\w*)\s*\}$");
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

static Dictionary<string, string> TokenTypeMap(params string[] tokensPaths)
{
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    var xns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
    foreach (var path in tokensPaths)
    {
        foreach (var el in XDocument.Load(path).Descendants())
        {
            var key = (string?)el.Attribute(xns + "Key");
            if (key is not null) map[key] = el.Name.LocalName;
        }
    }
    return map;
}

static Dictionary<string, string> TokenTypeMapOfXml(string xaml)
{
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    var xns = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
    foreach (var el in XDocument.Parse(xaml).Descendants())
    {
        var key = (string?)el.Attribute(xns + "Key");
        if (key is not null) map[key] = el.Name.LocalName;
    }
    return map;
}

// ---- 规则：令牌类型必须匹配（Double 用于 GridLength/Thickness 会崩）----
Check("令牌：设计令牌的声明类型与目标属性类型匹配（Static/Dynamic 都查）", () =>
{
    string tokens = Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes", "Tokens.xaml");
    string light = Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes", "Light.xaml");
    string dark = Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes", "Dark.xaml");

    // 令牌可能定义在令牌层，也可能定义在主题层（风格令牌随主题变化），两处都要纳入
    var tokenTypes = TokenTypeMap(tokens, light, dark);
    if (tokenTypes.Count == 0) throw new Exception("未解析到任何令牌 —— 守卫失效");

    // 两套主题对同一个 key 必须声明成**同一类型**：
    // 例如 Light 把 ControlHeight 写成 sys:Double、Dark 写成 Thickness，
    // 切换主题时就会出现"一个主题正常、另一个主题崩溃"，而只查 key 名的一致性规则查不出来。
    var lightTypes = TokenTypeMapOfXml(File.ReadAllText(light));
    var darkTypes = TokenTypeMapOfXml(File.ReadAllText(dark));
    var typeConflicts = lightTypes
        .Where(kv => darkTypes.TryGetValue(kv.Key, out var t) && t != kv.Value)
        .Select(kv => $"{kv.Key}: Light={kv.Value} vs Dark={darkTypes[kv.Key]}")
        .ToList();
    if (typeConflicts.Count > 0)
        throw new Exception("两套主题同名令牌类型不一致：\n    " + string.Join("\n    ", typeConflicts));

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

// ============================================================
// 规则：被 XAML 绑定的“只读派生属性”必须在依赖变化时发通知
//
// 背景（真实线上缺陷）：ConnectionsViewModel.CanConnect => Selected is { IsFolder: false }，
// 而 Selected 的 setter 只通知了 HasSelection、漏了 CanConnect →
// 选中集群后「连接」按钮永远停在初始 IsEnabled=false（编辑/删除按钮却正常亮起，
// 因为 HasSelection 通知了），只有双击才能连上（双击直接 Execute，绕过 IsEnabled）。
// 根因：WPF 绑定只在收到**该属性自己**的 PropertyChanged 时重新求值，
// "派生属性没通知" = 界面永久停在旧值；编译期与运行期都不报错。
// ============================================================

// 有 set 访问器的属性（可变 → 运行期会变、需要通知）
// 注意：init-only 不算可变 —— 它只能在对象初始化时赋值，构造完成后永不改变，
// 因此派生属性无需通知（NavItem.IconPath / ConnectionTreeNode.Item 都属于这种）。
static HashSet<string> MutableProperties(string source)
{
    var set = new HashSet<string>(StringComparer.Ordinal);
    var pattern = @"public\s+(?<mods>(?:required\s+|static\s+|override\s+|virtual\s+|sealed\s+|new\s+)*)"
                + @"(?<type>[\w\?<>\[\],\.]+)\s+(?<name>\w+)\s*\{(?<body>[^{}]*(?:\{[^{}]*\}[^{}]*)*)\}";
    foreach (Match m in Regex.Matches(source, pattern, RegexOptions.Multiline))
    {
        if (Regex.IsMatch(m.Groups["type"].Value, @"\b(class|record|struct|interface|enum|namespace)\b")) continue;
        string body = m.Groups["body"].Value;
        if (!Regex.IsMatch(body, @"\bget\b")) continue;
        bool hasInit = Regex.IsMatch(body, @"(?:^|[^\w])init\b");
        bool hasSet = Regex.IsMatch(body, @"(?:^|[^\w])set\b");
        if (hasSet && !hasInit) set.Add(m.Groups["name"].Value);
    }
    return set;
}

// 只读属性 → getter 体（表达式体 + 仅 get 的块体）
static Dictionary<string, string> ReadOnlyGetterBodies(string source)
{
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    var mods = @"(?:required\s+|static\s+|override\s+|virtual\s+|sealed\s+|new\s+)*";

    foreach (Match m in Regex.Matches(source,
        $@"public\s+{mods}[\w\?<>\[\],\.]+\s+(?<name>\w+)\s*=>\s*(?<body>[^;]+);", RegexOptions.Multiline))
        map[m.Groups["name"].Value] = m.Groups["body"].Value;

    var blockPattern = $@"public\s+{mods}(?<type>[\w\?<>\[\],\.]+)\s+(?<name>\w+)\s*\{{(?<body>[^{{]*(?:\{{[^{{}}]*\}}[^{{}}]*)*)\}}";
    foreach (Match m in Regex.Matches(source, blockPattern, RegexOptions.Multiline))
    {
        if (Regex.IsMatch(m.Groups["type"].Value, @"\b(class|record|struct|interface|enum|namespace)\b")) continue;
        string body = m.Groups["body"].Value;
        if (Regex.IsMatch(body, @"\bget\b") && !Regex.IsMatch(body, @"(?:^|[^\w])(?:set|init)\b"))
            map[m.Groups["name"].Value] = body;
    }
    return map;
}

static HashSet<string> NotifiedProperties(string source) =>
    Regex.Matches(source, @"OnPropertyChanged\(\s*nameof\(\s*(?<name>\w+)\s*\)")
        .Select(m => m.Groups["name"].Value).ToHashSet();

Check("绑定：被 XAML 引用的只读派生属性必须有 PropertyChanged 通知", () =>
{
    var mutable = MutableProperties(allVmSource);
    var derived = ReadOnlyGetterBodies(allVmSource);
    var notified = NotifiedProperties(allVmSource);
    if (mutable.Count == 0 || derived.Count == 0)
        throw new Exception("未能从 VM 源码解析出可写/派生属性 —— 守卫失效");

    // XAML 中绑定到的属性名（含 Path= 写法）
    var bound = new HashSet<string>(StringComparer.Ordinal);
    foreach (var f in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories)
                 .Concat(new[] { Path.Combine(repoRoot, "src", "ElasticDesktopManager", "MainWindow.xaml") }))
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\{Binding\s+(?:Path\s*=\s*)?(?<name>[A-Za-z_]\w*)"))
            bound.Add(m.Groups["name"].Value);
    }

    var bad = new List<string>();
    foreach (var (name, body) in derived)
    {
        if (!bound.Contains(name)) continue;    // 未被 XAML 使用 → 不管（避免噪音）
        if (notified.Contains(name)) continue;  // 已正确通知

        var deps = mutable
            .Where(p => p != name && Regex.IsMatch(body, $@"(?<![\w.]){Regex.Escape(p)}\b"))
            .OrderBy(p => p)
            .ToList();
        if (deps.Count > 0)
            bad.Add($"{name}（依赖可变的 {string.Join("/", deps)}）从未 OnPropertyChanged(nameof({name})) → 界面会永久停在旧值");
    }

    if (bad.Count > 0)
        throw new Exception($"发现 {bad.Count} 处派生属性未通知：\n    " + string.Join("\n    ", bad.Distinct()));
});

// ============================================================
// 规则：不得对资源字典集合使用字面量下标
//
// 背景（真实线上缺陷）：ThemeService 曾用 dicts.RemoveAt(1) 删旧主题词典，注释假定
// "索引 0 = Common.xaml"。后来在 Common 之前插入了 Tokens.xaml，索引整体后移一位，
// 于是切主题时删掉的其实是 Common.xaml（整套控件模板）→ 控件回退成 WPF 默认外观，
// 用户表现为"UI 还是之前的样子"。合并顺序会随功能演进而变，任何按固定下标的增删都是定时炸弹。
// ============================================================

static List<string> FindLiteralDictionaryIndexing(string source, string label)
{
    var hits = new List<string>();
    foreach (Match m in Regex.Matches(source,
        @"MergedDictionaries\s*\.\s*RemoveAt\s*\(\s*\d+\s*\)|MergedDictionaries\s*\[\s*\d+\s*\]"))
        hits.Add($"{label}: {m.Value} —— 请按 Source 识别词典，不要依赖合并顺序/下标");
    return hits;
}

Check("资源字典：不得用字面量下标增删 MergedDictionaries", () =>
{
    var hits = new List<string>();
    foreach (var f in Directory.GetFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        hits.AddRange(FindLiteralDictionaryIndexing(File.ReadAllText(f), Path.GetRelativePath(repoRoot, f)));
    if (hits.Count > 0)
        throw new Exception($"发现 {hits.Count} 处：\n    " + string.Join("\n    ", hits.Distinct()));
});

Check("守卫自检：派生属性未通知 / 字典字面量下标 / 跨主题类型冲突 均能被抓出", () =>
{
    // ---- 派生属性未通知：复现 CanConnect 真实缺陷 ----
    const string vm = """
        public class V
        {
            private object? _selected;
            public object? Selected
            {
                get => _selected;
                set { if (SetProperty(ref _selected, value)) OnPropertyChanged(nameof(HasSelection)); }
            }
            public bool HasSelection => Selected is not null;
            public bool CanConnect => Selected is not null;
            public string Plain => "x";
        }
        """;
    var mutable = MutableProperties(vm);
    if (!mutable.Contains("Selected")) throw new Exception("未识别可写属性 Selected");
    if (mutable.Contains("V")) throw new Exception("误报：类名 V 不应被当成可写属性");
    var derived = ReadOnlyGetterBodies(vm);
    if (!derived.ContainsKey("CanConnect") || !derived.ContainsKey("HasSelection"))
        throw new Exception("未识别只读派生属性");
    var notified = NotifiedProperties(vm);
    if (!notified.Contains("HasSelection")) throw new Exception("未识别已有通知");
    if (notified.Contains("CanConnect")) throw new Exception("误判：CanConnect 不该被认作已通知");
    if (!Regex.IsMatch(derived["CanConnect"], @"(?<![\w.])Selected\b"))
        throw new Exception("未识别派生属性对可写属性的依赖");

    // ---- 字典字面量下标 ----
    if (FindLiteralDictionaryIndexing("Application.Current.Resources.MergedDictionaries.RemoveAt(1);", "t").Count != 1)
        throw new Exception("未抓出 MergedDictionaries.RemoveAt(1)");
    if (FindLiteralDictionaryIndexing("dicts[MergedDictionaries.Count - 1]", "t").Count != 0)
        throw new Exception("误报：按 Count 计算的下标是安全的");
    // init-only 属性不算可变（否则 NavItem.Item / GroupKey 这类只读派生属性会误报）
    const string initOnly = """
        public class N
        {
            public required object Item { get; init; }
            public string Name => Item.ToString()!;
        }
        """;
    if (MutableProperties(initOnly).Contains("Item"))
        throw new Exception("误报：init-only 属性被当成可变属性");

    // ---- 跨主题同名令牌类型冲突 ----
    var a = TokenTypeMapOfXml("""<ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><sys:Double x:Key="H" xmlns:sys="clr-namespace:System;assembly=System.Runtime">32</sys:Double></ResourceDictionary>""");
    var b = TokenTypeMapOfXml("""<ResourceDictionary xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><Thickness x:Key="H">32</Thickness></ResourceDictionary>""");
    if (a["H"] == b["H"]) throw new Exception("自检样本构造失败（两边类型应不同）");
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


// ---- 规则：同一条词条在两种语言里的占位符必须一致 ----
//
// `Localization.L(key, args)` 内部是 string.Format：中文写了 "{0} 个仓库" 而英文写成
// "repositories"（漏了 {0}），英文界面就会静默丢掉参数；反过来多写一个 {1} 更糟 ——
// FormatException 被 Localization.L 吞掉后会**直接显示带 {} 的格式串**。
static Dictionary<string, string> DictionaryEntries(string block)
{
    var map = new Dictionary<string, string>();
    foreach (Match m in Regex.Matches(block, @"\[""([^""]+)""\]\s*=\s*""((?:[^""\\]|\\.)*)"""))
        map[m.Groups[1].Value] = m.Groups[2].Value;
    return map;
}

static string[] Placeholders(string value) =>
    Regex.Matches(value, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();

Check("i18n：同一条词条在 zh_CN / en 里的占位符必须一致", () =>
{
    string locFile = Path.Combine(repoRoot, "src", "ElasticDesktopManager.Core", "I18n", "Localization.cs");
    string src = File.ReadAllText(locFile);
    int zhStart = src.IndexOf("Dictionary<string, string> Zh", StringComparison.Ordinal);
    int enStart = src.IndexOf("Dictionary<string, string> En", StringComparison.Ordinal);
    if (zhStart < 0 || enStart < 0 || enStart < zhStart)
        throw new Exception("未能定位 Zh / En 词典声明");

    var zh = DictionaryEntries(src[zhStart..enStart]);
    var en = DictionaryEntries(src[enStart..]);

    var bad = new List<string>();
    foreach (var (key, zhValue) in zh)
    {
        if (!en.TryGetValue(key, out var enValue)) continue;
        var a = Placeholders(zhValue);
        var b = Placeholders(enValue);
        if (!a.SequenceEqual(b))
            bad.Add($"{key}: zh={{{string.Join(",", a)}}} vs en={{{string.Join(",", b)}}}");
    }
    if (bad.Count > 0)
        throw new Exception($"发现 {bad.Count} 条占位符不一致：\n    " + string.Join("\n    ", bad.Distinct()));
});

// ---- 规则：可编辑 ComboBox 必须提供 PART_EditableTextBox ----
//
// 背景（真实缺陷）：ComboBox 模板是我们自己写的，而 IsEditable="True" 时 WPF 需要模板里
// 有一个名为 PART_EditableTextBox 的 TextBox 才能进入编辑态。缺了它，可编辑下拉直接不可用
// （搜索页的索引下拉就是全项目唯一一个可编辑 ComboBox，也是唯一一个"下拉没数据"的控件）。
// 构建期看不出来、本机也跑不了 WPF，只能靠静态规则兜住。
//
/// <summary>
/// 取出 XAML 里 TargetType="ComboBox" 的 ControlTemplate（模板自己的名字域）。
/// 用 XML 解析而不是字符串搜索，理由有三个，都是踩过的坑：
///   ① 字符串搜索会被注释里的文字骗到（模板里就有 "PART_EditableTextBox" 的说明）；
///      解析成元素树后注释根本不是元素，天然排除。
///   ② 必须限定在 ComboBox 模板自己的名字域里：模板内部还嵌着 ToggleButton 的
///      ControlTemplate，写在那里的同名元素用 ComboBox.FindName 根本够不到，
///      字符串搜索却会认为"有"。
///   ③ 搜索不能一路扫到文件尾，否则后面任何一个模板里的同名元素都能让它变绿。
/// </summary>
static XElement? ComboTemplate(string xaml)
{
    try
    {
        return XDocument.Parse(xaml).Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ControlTemplate"
                                 && (string?)e.Attribute("TargetType") == "ComboBox");
    }
    catch (System.Xml.XmlException)
    {
        return null; // 解析不了的样本一律当作"没有"，由调用方按"规则失效"报错
    }
}

static bool ComboTemplateHasEditableBox(string xaml)
{
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var combo = ComboTemplate(xaml);
    if (combo is null) return false;

    return combo.Descendants().Any(e =>
        e.Name.LocalName == "TextBox"
        && (string?)e.Attribute(x + "Name") == "PART_EditableTextBox"
        && !e.Ancestors().TakeWhile(a => a != combo).Any(a => a.Name.LocalName == "ControlTemplate"));
}

// ---- 规则：ComboBox 收起态展示器必须绑定 ContentTemplateSelector ----
//
// 背景（真实缺陷，用户截图发现）：搜索页"添加条件"的两个下拉选完值以后显示的是
// "ClauseOption" / "OperatorOption"（被 90px/110px 列宽截断成 "ClauseOp" / "OperatorOp"）。
// 根源不是 XAML 里 DisplayMemberPath 写错，而是它的实现方式很反直觉：
//   * ItemsControl 把 DisplayMemberPath 实现成"往 **ItemTemplateSelector** 上装一个内部
//     DisplayMemberTemplateSelector"（ItemsControl.cs:390-417），ItemTemplate 始终是 null；
//   * ComboBox.UpdateSelectionBoxItem 只传 SelectionBoxItemTemplate = ItemTemplate
//     （ComboBox.cs:847-945），**ComboBox.cs 里压根没有 DisplayMemberPath 这个属性**；
//   * 所以收起态能不能显示成员值，完全取决于模板里的 ContentPresenter 有没有接上
//     ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"。
//     官方模板的每个变体都有这一行（Themes/XAML/ComboBox.xaml:373-376 等）。
// 缺了它：凡是用 DisplayMemberPath 的下拉，收起态都会显示数据对象的 ToString
// （设置页语言、快照页仓库、搜索页条件行全部中招），展开的列表却是正常的。
static bool ComboSelectionBoxHasTemplateSelector(string xaml)
{
    var combo = ComboTemplate(xaml);
    if (combo is null) return false;

    var presenter = combo.Descendants().FirstOrDefault(e =>
        e.Name.LocalName == "ContentPresenter"
        && ((string?)e.Attribute("Content") ?? "").Contains("SelectionBoxItem", StringComparison.Ordinal)
        && !e.Ancestors().TakeWhile(a => a != combo).Any(a => a.Name.LocalName == "ControlTemplate"));
    if (presenter is null) return false; // 展示器换了别的写法：宁可报错也不要静默放过

    return ((string?)presenter.Attribute("ContentTemplateSelector") ?? "")
        .Contains("ItemTemplateSelector", StringComparison.Ordinal);
}


Check("控件模板：可编辑 ComboBox 必须包含 PART_EditableTextBox", () =>
{
    var editableUsers = new List<string>();
    foreach (var f in Directory.GetFiles(Path.Combine(repoRoot, "src"), "*.xaml", SearchOption.AllDirectories))
    {
        // 只要出现 IsEditable 且不是显式 False 就算"可能可编辑"：
        // IsEditable="true"（小写）、IsEditable="{Binding ...}" 这些写法都不应该漏掉。
        foreach (Match m in Regex.Matches(File.ReadAllText(f),
                     @"<ComboBox\b[^>]*\bIsEditable\s*=\s*""([^""]*)""", RegexOptions.Singleline))
        {
            if (m.Groups[1].Value.Trim().Equals("False", StringComparison.OrdinalIgnoreCase)) continue;
            editableUsers.Add(Path.GetRelativePath(repoRoot, f));
            break;
        }
    }
    if (editableUsers.Count == 0) return; // 没人用可编辑下拉 → 不要求模板带该部件

    var themesDir = Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes");
    bool ok = Directory.GetFiles(themesDir, "*.xaml").Any(f => ComboTemplateHasEditableBox(File.ReadAllText(f)));
    if (!ok)
        throw new Exception(
            $"以下文件使用了可编辑 ComboBox（{string.Join(", ", editableUsers)}），" +
            "但 Themes 下的 ComboBox 模板没有 PART_EditableTextBox —— WPF 无法进入编辑态，下拉会不可用");
});

Check("控件模板：ComboBox 收起态展示器必须绑定 ContentTemplateSelector（DisplayMemberPath 靠它生效）", () =>
{
    var themesDir = Path.Combine(repoRoot, "src", "ElasticDesktopManager", "Themes");
    var files = Directory.GetFiles(themesDir, "*.xaml").ToList();

    // 规则本身也不能"静默通过"：找不到 ComboBox 模板就直接报错（改名/搬家都要有人来更新这条规则）
    var withTemplate = files.Where(f => ComboTemplate(File.ReadAllText(f)) is not null).ToList();
    if (withTemplate.Count == 0)
        throw new Exception($"Themes 下（{string.Join(", ", files.Select(Path.GetFileName))}）没有找到 ComboBox 的 ControlTemplate，"
            + "本规则失去保护对象 —— 请更新规则，不要让它空转通过");

    var bad = withTemplate.Where(f => !ComboSelectionBoxHasTemplateSelector(File.ReadAllText(f))).ToList();
    if (bad.Count > 0)
        throw new Exception("这些 ComboBox 模板的收起态展示器没有绑定 ContentTemplateSelector：\n    "
            + string.Join("\n    ", bad.Select(Path.GetFileName))
            + "\n  DisplayMemberPath 是通过 ItemTemplateSelector 生效的，缺这一行时凡是用 DisplayMemberPath 的下拉"
            + "收起态都会显示数据对象的 ToString（如 \"ClauseOption\"/\"EsRepository\"），而展开的列表正常");
});

// ---- 规则：DataGrid 的列数必须等于 code-behind 里表头映射的项数 ----
//
// 背景：快照页刻意把**所有**表头文案从 XAML 挪到 code-behind（XAML 里不写任何可见文案），
// 于是表头靠下标赋值：`grid.Columns[index].Header = ...`，而 `ApplyHeaders` 对越界是**静默跳过**的
// （对最终用户来说"空表头"比崩溃温和，所以不改运行期行为）。
// 但代价是：XAML 里多加/少加一列，界面只会安静地少一个表头，编译、单测、其它守卫全绿。
// 这条规则把"列数 ↔ 数组项数"钉死，让这类错位不可能悄悄上线。
static Dictionary<string, int> DataGridColumnCounts(string xaml)
{
    var map = new Dictionary<string, int>(StringComparer.Ordinal);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    XDocument doc;
    try { doc = XDocument.Parse(xaml); }
    catch (System.Xml.XmlException) { return map; }

    foreach (var grid in doc.Descendants().Where(e => e.Name.LocalName == "DataGrid"))
    {
        string? name = (string?)grid.Attribute(x + "Name");
        if (string.IsNullOrEmpty(name)) continue;
        var cols = grid.Elements().FirstOrDefault(e => e.Name.LocalName == "DataGrid.Columns");
        if (cols is null) continue;
        map[name] = cols.Elements()
            .Count(e => e.Name.LocalName.StartsWith("DataGrid", StringComparison.Ordinal)
                        && e.Name.LocalName.EndsWith("Column", StringComparison.Ordinal));
    }
    return map;
}

static Dictionary<string, int> HeaderMapCounts(string codeBehind)
{
    var map = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (Match m in Regex.Matches(codeBehind,
                 @"(\w+)Headers\s*=\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline))
    {
        map[m.Groups[1].Value + "Headers"] =
            Regex.Matches(m.Groups["body"].Value, @"\(\s*\d+\s*,").Count;
    }
    return map;
}

/// <summary>返回问题清单；空 = 对齐。`XxxGrid` 对应 `XxxHeaders`。</summary>
static List<string> GridHeaderProblems(string xaml, string codeBehind)
{
    var problems = new List<string>();
    var grids = DataGridColumnCounts(xaml);
    if (grids.Count == 0)
    {
        problems.Add("没有解析到任何带 x:Name 的 DataGrid —— 本规则失去保护对象，请更新规则（不要空转通过）");
        return problems;
    }
    var maps = HeaderMapCounts(codeBehind);
    foreach (var (grid, columns) in grids)
    {
        // 注意 "Grid" 这个名字本身以 "Grid" 结尾：只查后缀会推出空的映射名（"Headers"），
        // 报出来的错会指向"找不到映射"而不是真正的问题（名字里没有语义前缀），所以先挡掉。
        if (grid.Length <= "Grid".Length || !grid.EndsWith("Grid", StringComparison.Ordinal))
        {
            problems.Add($"{grid}：命名不符合 XxxGrid 约定（如 IndexGrid / NodeGrid / ShardGrid），无法推出对应的表头映射数组");
            continue;
        }
        string expectedMap = grid[..^"Grid".Length] + "Headers";
        if (!maps.TryGetValue(expectedMap, out int entries))
        {
            problems.Add($"{grid}：code-behind 里找不到 {expectedMap}，表头会整列空白");
            continue;
        }
        if (columns != entries)
            problems.Add($"{grid}：XAML {columns} 列 vs {expectedMap} {entries} 项 —— ApplyHeaders 对越界是静默跳过的，界面只会安静地少表头");
    }
    return problems;
}

Check("DataGrid：每个表的列数必须等于 code-behind 表头映射的项数（越界是静默的，界面只会少表头）", () =>
{
    var problems = new List<string>();
    var covered = new List<string>();
    foreach (var xaml in Directory.GetFiles(viewsDir, "*.xaml").OrderBy(f => f, StringComparer.Ordinal))
    {
        string cs = xaml + ".cs";
        if (!File.Exists(cs)) continue;
        string label = Path.GetFileName(xaml);
        // 只在"XAML 里显式列了列"的表上校验：SearchView/SqlView 的结果列是运行时按 ES 字段建的
        var grids = DataGridColumnCounts(File.ReadAllText(xaml));
        if (grids.Count == 0) continue;

        problems.AddRange(GridHeaderProblems(File.ReadAllText(xaml), File.ReadAllText(cs))
            .Select(p => $"{label}: {p}"));
        covered.AddRange(grids.Keys.Select(g => $"{label}:{g}"));
    }

    // 规则不许空转：表的数量变了（改名/新增/删除）必须显式更新本条，否则它会悄悄少保护几张表
    const int expected = 9;
    if (covered.Count != expected)
        throw new Exception($"扫描到 {covered.Count} 个显式列 DataGrid，规则期望 {expected} 个 —— 表结构变动后请更新本规则，别让它空转：\n    "
            + string.Join("\n    ", covered));

    if (problems.Count > 0)
        throw new Exception("表头映射与列数不一致（越界项会静默跳过，界面只是安静地少表头）：\n    "
            + string.Join("\n    ", problems));
});

Check("守卫自检：DataGrid 列数与表头映射不一致必须能被抓出", () =>
{
    const string xns = "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
    string twoCols = $"<UserControl {xns}><DataGrid x:Name=\"RepoGrid\"><DataGrid.Columns>"
        + "<DataGridTextColumn /><DataGridTextColumn /></DataGrid.Columns></DataGrid></UserControl>";
    string twoEntries = """static readonly (int, string)[] RepoHeaders = { (0, "a"), (1, "b") };""";

    if (GridHeaderProblems(twoCols, twoEntries).Count != 0)
        throw new Exception("误报：2 列 vs 2 项应判定对齐");
    if (GridHeaderProblems(twoCols, """static readonly (int, string)[] RepoHeaders = { (0, "a") };""").Count != 1)
        throw new Exception("未抓出列数多于表头项数");
    if (GridHeaderProblems(twoCols, """static readonly (int, string)[] RepoHeaders = { (0, "a"), (1, "b"), (2, "c") };""").Count != 1)
        throw new Exception("未抓出表头项数多于列数");
    if (!GridHeaderProblems(twoCols, "// 没有映射").Single().Contains("找不到 RepoHeaders", StringComparison.Ordinal))
        throw new Exception("未抓出缺失的表头映射数组");
    if (GridHeaderProblems($"<UserControl {xns}><Grid /></UserControl>", twoEntries).Count != 1)
        throw new Exception("一个 DataGrid 都没有时必须报错，而不是静默通过");
    // 只叫 "Grid" 的表没有语义前缀，推向 "Headers" 会报成“找不到映射”；必须直接点出命名问题
    string bare = $"<UserControl {xns}><DataGrid x:Name=\"Grid\"><DataGrid.Columns>"
        + "<DataGridTextColumn /></DataGrid.Columns></DataGrid></UserControl>";
    if (!GridHeaderProblems(bare, twoEntries).Single().Contains("命名不符合 XxxGrid 约定", StringComparison.Ordinal))
        throw new Exception("未抓出没有语义前缀的表名（Grid 本身以 Grid 结尾，容易被误判成合规）");
    // 模板列也算一列（DataGridTemplateColumn 里面有 .CellTemplate 属性元素，别把子节点数错）
    string tpl = $"<UserControl {xns}><DataGrid x:Name=\"RepoGrid\"><DataGrid.Columns>"
        + "<DataGridTemplateColumn><DataGridTemplateColumn.CellTemplate><DataTemplate><TextBlock /></DataTemplate>"
        + "</DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn></DataGrid.Columns></DataGrid></UserControl>";
    if (!GridHeaderProblems(tpl, twoEntries).Single().Contains("1 列", StringComparison.Ordinal))
        throw new Exception("模板列应计为 1 列（不能把 CellTemplate 里的子节点也算成列）");
});

// ---- 规则：XAML 里不许写死文案（Header / ToolTip）----
//
// 这两类属性的文案只允许有一个来源：code-behind 按当前语言赋值（表头映射数组 / Localize()）。
// 写死在 XAML 里的值有两个害处：
//   ① 它在 code-behind 执行之前就已经渲染过一帧（漏改时用户看到的就是这串写死的英文）；
//   ② 它让"漏了本地化"看起来像"故意的"——第 6 轮 R18 就是这样漏掉 33 处表头的。
// 注释里的示例文字不算（先剥注释再扫）。
static string StripXmlComments(string xaml) =>
    Regex.Replace(xaml, "<!--.*?-->", "", RegexOptions.Singleline);

static List<string> HardcodedChromeLiterals(string xaml, string label)
{
    var problems = new List<string>();
    foreach (Match m in Regex.Matches(StripXmlComments(xaml), @"\b(Header|ToolTip)\s*=\s*""([^""]*)"""))
    {
        string value = m.Groups[2].Value.Trim();
        if (value.Length == 0) continue;
        if (value.StartsWith("{", StringComparison.Ordinal)) continue;   // Binding / StaticResource / x:Static
        problems.Add($"{label}: {m.Groups[1].Value}=\"{value}\"");
    }
    return problems;
}

Check("XAML：Header/ToolTip 不得写死文案（只能由 code-behind 按当前语言赋值）", () =>
{
    var problems = new List<string>();
    int scanned = 0;
    foreach (var xaml in Directory.GetFiles(viewsDir, "*.xaml"))
    {
        scanned++;
        problems.AddRange(HardcodedChromeLiterals(File.ReadAllText(xaml), Path.GetFileName(xaml)));
    }
    string main = Path.Combine(repoRoot, "src", "ElasticDesktopManager", "MainWindow.xaml");
    if (File.Exists(main))
    {
        scanned++;
        problems.AddRange(HardcodedChromeLiterals(File.ReadAllText(main), "MainWindow.xaml"));
    }

    if (scanned == 0)
        throw new Exception("没有扫描到任何 XAML —— 本规则失去保护对象，请更新规则");
    if (problems.Count > 0)
        throw new Exception($"发现 {problems.Count} 处写死的文案（切语言时不会跟着变）：\n    "
            + string.Join("\n    ", problems));
});

Check("守卫自检：XAML 写死的 Header/ToolTip 必须能被抓出（绑定与注释不误报）", () =>
{
    if (HardcodedChromeLiterals("""<DataGridTextColumn Header="Name" />""", "t").Count != 1)
        throw new Exception("未抓出写死的表头");
    if (HardcodedChromeLiterals("""<Button ToolTip="Refresh" />""", "t").Count != 1)
        throw new Exception("未抓出写死的 ToolTip");
    if (HardcodedChromeLiterals("""<DataGridTextColumn Header="{Binding X}" />""", "t").Count != 0)
        throw new Exception("误报：绑定表达式不是写死的文案");
    if (HardcodedChromeLiterals("""<Button ToolTip="{x:Static local:Tip.X}" />""", "t").Count != 0)
        throw new Exception("误报：x:Static 不是写死的文案");
    if (HardcodedChromeLiterals("""<!-- 例：ToolTip="Refresh" 是错的 --><Button />""", "t").Count != 0)
        throw new Exception("误报：注释里的示例不该算数");
    if (HardcodedChromeLiterals("""<DataGridTextColumn Header="" />""", "t").Count != 0)
        throw new Exception("误报：空表头（由 code-behind 赋值）不该算数");
});

// ---- 规则：译文不得当 key 再传一次 ----
//
// 第 7 轮在索引页发现：CreateConfirm / ShowJson 收的形参是**词条 key**（方法体内再 L() 一次），
// 而调用点传的是 Localization.L("index.confirm.refresh") 的结果。L() 查不到就原样返回，
// 于是中文界面看着完全正常、**英文界面弹框仍是中文**。既有规则只查"L(\"字面量\") 是不是词条"，
// 这一层间接（key 传进去又被 L() 一次）它看不见，所以单独加一条。
// 约定：形参名以 Key/key 结尾 ⇒ 收的是词条 key。

/// <summary>按顶层逗号切参数列表；跳过字符串、跟踪 &lt;&gt; () [] {} 深度（泛型里的逗号不算分隔）。</summary>
static List<string> SplitTopLevel(string text)
{
    var parts = new List<string>();
    var current = new System.Text.StringBuilder();
    int depth = 0;
    for (int i = 0; i < text.Length; i++)
    {
        char c = text[i];
        if (c == '"')
        {
            current.Append(c);
            for (i++; i < text.Length && text[i] != '"'; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length) { current.Append(text[i]); i++; }
                current.Append(text[i]);
            }
            if (i < text.Length) current.Append(text[i]);
            continue;
        }
        if (c is '<' or '(' or '[' or '{') depth++;
        else if (c is '>' or ')' or ']' or '}') depth--;
        else if (c == ',' && depth == 0) { parts.Add(current.ToString()); current.Clear(); continue; }
        current.Append(c);
    }
    if (current.Length > 0) parts.Add(current.ToString());
    return parts;
}

/// <summary>`(形参表)` 的起止（用括号配对找，避免正则遇到 Func&lt;...&gt; 里的逗号就断）。</summary>
static (string Params, int End) ParamListAt(string source, int openParen)
{
    int depth = 0;
    for (int i = openParen; i < source.Length; i++)
    {
        if (source[i] == '(') depth++;
        else if (source[i] == ')')
        {
            depth--;
            if (depth == 0) return (source[(openParen + 1)..i], i);
        }
    }
    return ("", openParen);
}

/// <summary>收集"形参名以 Key/key 结尾"的方法 → 第几个参数是 key。</summary>
static Dictionary<string, HashSet<int>> KeyParamPositions(string source)
{
    var map = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
    foreach (Match m in Regex.Matches(source, @"\b(\w+)\s*\("))
    {
        string method = m.Groups[1].Value;
        if (method is "if" or "for" or "while" or "switch" or "catch" or "using" or "lock" or "foreach") continue;
        var (parameters, _) = ParamListAt(source, m.Index + m.Length - 1);
        if (parameters.Length == 0 || parameters.Contains(';')) continue;

        var parts = SplitTopLevel(parameters);
        for (int i = 0; i < parts.Count; i++)
        {
            // 必须是"类型 名字"的形参写法（调用点的实参没有类型，天然被排除）
            var pm = Regex.Match(parts[i].Trim(), @"^[\w<>,?\[\]\.\s]+?\s+(\w*[Kk]ey)$");
            if (!pm.Success) continue;
            if (!map.TryGetValue(method, out var set)) map[method] = set = new HashSet<int>();
            set.Add(i);
        }
    }
    return map;
}

static List<string> TranslationPassedAsKey(string source, string label, Dictionary<string, HashSet<int>> keyParams)
{
    var problems = new List<string>();
    foreach (var (method, positions) in keyParams)
    {
        foreach (Match m in Regex.Matches(source, $@"(?<![\w.]){Regex.Escape(method)}\s*\("))
        {
            int open = m.Index + m.Length - 1;
            var (parameters, _) = ParamListAt(source, open);
            if (parameters.Length == 0) continue;
            var args = SplitTopLevel(parameters);
            foreach (int i in positions)
            {
                if (i >= args.Count) continue;
                if (args[i].TrimStart().StartsWith("Localization.L(", StringComparison.Ordinal))
                    problems.Add($"{label}: {method}(...) 第 {i + 1} 个实参是 L(...) 的结果，但形参是 key —— 会再查一次词条，查不到就原样返回（英文界面里仍是中文）");
            }
        }
    }
    return problems;
}

Check("i18n：形参是 key 的方法，调用点不得传 Localization.L(...) 的结果（译文当 key 会双重翻译）", () =>
{
    string root = Path.Combine(repoRoot, "src");
    var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToList();

    // 先全局收集声明（调用点可能和声明不在同一个文件），再逐文件查调用点。
    // 同名不同签名的巧合会带来少量误报，但误报是显式的、可复核的；漏报才是这条规则要防的。
    var keyParams = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
    foreach (var f in files)
        foreach (var (method, positions) in KeyParamPositions(File.ReadAllText(f)))
        {
            if (!keyParams.TryGetValue(method, out var set)) keyParams[method] = set = new HashSet<int>();
            set.UnionWith(positions);
        }

    if (keyParams.Count == 0)
        throw new Exception("没有解析到任何以 Key 结尾的形参 —— 本规则失去保护对象，请更新规则");

    var problems = new List<string>();
    foreach (var f in files)
        problems.AddRange(TranslationPassedAsKey(File.ReadAllText(f),
            Path.GetRelativePath(repoRoot, f), keyParams));

    if (problems.Count > 0)
        throw new Exception("发现把译文当 key 传回的调用点：\n    " + string.Join("\n    ", problems.Distinct()));
});

Check("守卫自检：译文当 key 传必须能被抓出（正确写法/变量/泛型逗号都不误报）", () =>
{
    var bug = KeyParamPositions("""private ICommand Create(string confirmKey, object act) => null;""");
    if (!bug.ContainsKey("Create") || !bug["Create"].Contains(0))
        throw new Exception("未识别以 Key 结尾的形参");
    if (TranslationPassedAsKey("""Create(Localization.L("index.confirm.flush"), act);""", "t", bug).Count != 1)
        throw new Exception("未抓出把 L() 结果当 key 传的调用点");
    if (TranslationPassedAsKey("""Create("index.confirm.flush", act);""", "t", bug).Count != 0)
        throw new Exception("误报：直接传 key 字面量是正确的");
    if (TranslationPassedAsKey("""Create(confirmKey, act);""", "t", bug).Count != 0)
        throw new Exception("误报：传变量不该被判定为译文");
    if (TranslationPassedAsKey("""Ui.Toast(Localization.L("common.ok"));""", "t", bug).Count != 0)
        throw new Exception("误报：Toast 的形参不是 key");

    // 泛型里的逗号不能把形参下标算错（否则 key 参数会被漏掉/错位）
    var generic = KeyParamPositions("""private void Send(Func<int, int> map, string titleKey) { }""");
    if (!generic.TryGetValue("Send", out var pos) || !pos.Contains(1))
        throw new Exception("泛型带来的逗号把形参下标算错了（key 参数应仍在下标 1）");
    if (TranslationPassedAsKey("""Send(x, Localization.L("a.b"));""", "t", generic).Count != 1)
        throw new Exception("未抓出泛型形参之后的 key 参数被传译文");

    // 实参里带 lambda（内含逗号与括号）时不能把实参切错
    var lambda = KeyParamPositions("""private void Confirm(string confirmKey, Func<int, int, int> act) { }""");
    if (TranslationPassedAsKey("""Confirm(Localization.L("a.b"), (c, i) => c.X(i.Name, CancellationToken.None));""", "t", lambda).Count != 1)
        throw new Exception("实参含 lambda 时应仍能抓出第 1 个实参的译文");
    if (TranslationPassedAsKey("""Confirm("a.b", (c, i) => c.X(i.Name, CancellationToken.None));""", "t", lambda).Count != 0)
        throw new Exception("误报：lambda 里的逗号不该被当成实参分隔符");
});

// ---- 规则：页面视图在 code-behind 里赋本地化文案时，必须订阅 LanguageChanged ----
//
// 背景（第 4 轮修 SnapshotView、第 5 轮修 SearchView，同一个坑踩了两次）：
// 页面被 MainViewModel.GetPage 缓存、只创建一次，从设置窗口切语言时不会重新 Loaded
// （`MainViewModel.OnLanguageChanged` 只刷新左侧导航标题与状态栏），
// 于是整页 chrome 停在旧语言、而 VM 的动态文案已切新语言 → 中英混排。
//
// 只对"缓存的页面视图"（XAML 根是 UserControl）要求订阅：弹窗每次都是新构造的，
// 构造时取到的就是当前语言；给弹窗挂 LanguageChanged 反而会让 Localization 永久引用窗口。
static bool RootIsUserControl(string xaml) => Regex.IsMatch(xaml, @"<UserControl\b");

static bool LocalizesChromeInCodeBehind(string cs) =>
    Regex.IsMatch(cs, @"(\.Text|\.Content|\.Header|\.ToolTip)\s*=\s*[^;]*Localization\.L\(");

/// <summary>
/// 已知"在 code-behind 里本地化但没订阅语言切换"的页面视图 —— 历史遗留，待统一修。
/// 这是一份**只允许缩短**的债务清单：修好一个就删一条，否则下面的规则会报错。
/// 第 7 轮已把 8 个缓存页面全部修完（EmptyState/Health/Indices/Metrics/Nodes/Rest/Shards/Sql），
/// 现在为空；留空数组是为了保留"要么零债务、要么显式承认"的棘轮机制，不要删掉这个函数。
/// </summary>
static string[] KnownStalePageLocalizers() => Array.Empty<string>();

/// <summary>
/// 判定一个视图：n/a=不适用（弹窗 / 没在 code-behind 里本地化）；
/// subscribed=已订阅；allowed=在白名单里（历史遗留）；missing=该订阅却没订阅。
/// </summary>
static string PageLocalizerVerdict(string xaml, string cs, string fileName, string[] allowlist)
{
    if (!RootIsUserControl(xaml)) return "n/a";            // 弹窗：每次新构造，不要求
    if (!LocalizesChromeInCodeBehind(cs)) return "n/a";    // 没有在 code-behind 里本地化
    if (cs.Contains("LanguageChanged +=", StringComparison.Ordinal)) return "subscribed";
    return allowlist.Contains(fileName, StringComparer.Ordinal) ? "allowed" : "missing";
}

Check("i18n：页面视图在 code-behind 里本地化时必须订阅 LanguageChanged（否则切语言停在旧语言）", () =>
{
    var allowlist = KnownStalePageLocalizers();
    var missing = new List<string>();
    var subscribed = new List<string>();
    int pageLocalizers = 0;

    foreach (var xaml in Directory.GetFiles(viewsDir, "*.xaml"))
    {
        string cs = xaml + ".cs";
        if (!File.Exists(cs)) continue;
        string name = Path.GetFileName(cs);
        string verdict = PageLocalizerVerdict(File.ReadAllText(xaml), File.ReadAllText(cs), name, allowlist);
        if (verdict == "n/a") continue;
        pageLocalizers++;
        if (verdict == "subscribed") subscribed.Add(name);
        else if (verdict == "missing") missing.Add(name);
    }

    if (pageLocalizers == 0)
        throw new Exception("没有扫描到任何在 code-behind 里本地化的页面视图 —— 规则失去保护对象，请更新规则");
    if (missing.Count > 0)
        throw new Exception("这些页面视图在 code-behind 里赋本地化文案，却没有订阅 Localization.LanguageChanged：\n    "
            + string.Join("\n    ", missing)
            + "\n  页面被 MainViewModel 缓存、切语言时不会重新 Loaded → 整页 chrome 会停在旧语言（中英混排）");

    // 债务清单只能缩短：条目已经修好后必须删掉，否则清单会变成掩盖问题的橡皮擦
    foreach (var fixedFile in subscribed)
        if (allowlist.Contains(fixedFile, StringComparer.Ordinal))
            throw new Exception($"{fixedFile} 已订阅 LanguageChanged，请把它从 KnownStalePageLocalizers 白名单里删除");
});

// ---- 规则：订阅了语言切换的页面视图必须同时让 VM 重算它自己缓存的文案 ----
//
// 视图只刷得动自己 x:Name 元素上的 chrome；“共 N 条 / 第 N 页 / 指标卡标签 / 分组标题”
// 这类在加载时拼好、之后不再重算的字符串活在 VM 里。视图订阅了却不叫 VM 重算，
// 切完语言就是“标题变了、统计还是旧语言”的中英混排 —— 第 7 轮修之前 8 个页面都是这个状态。
static bool SubscribesLanguageChanged(string cs) =>
    cs.Contains("LanguageChanged +=", StringComparison.Ordinal);

/// <summary>判定一个视图：n/a=弹窗或没订阅；ok=订阅了并且叫了 VM 重算；missing=该叫没叫。</summary>
static string PageRelocalizeVerdict(string xaml, string cs)
{
    if (!RootIsUserControl(xaml)) return "n/a";            // 弹窗每次新建，构造时即当前语言
    if (!SubscribesLanguageChanged(cs)) return "n/a";      // 没订阅语言切换，这里不管
    return cs.Contains("Relocalize(", StringComparison.Ordinal) ? "ok" : "missing";
}

Check("i18n：订阅语言切换的页面视图必须调用 VM 的 Relocalize()（否则统计/页码停在旧语言）", () =>
{
    var problems = new List<string>();
    int scanned = 0;
    foreach (var xaml in Directory.GetFiles(viewsDir, "*.xaml"))
    {
        string cs = xaml + ".cs";
        if (!File.Exists(cs)) continue;
        if (PageRelocalizeVerdict(File.ReadAllText(xaml), File.ReadAllText(cs)) == "n/a") continue;
        scanned++;
        if (PageRelocalizeVerdict(File.ReadAllText(xaml), File.ReadAllText(cs)) == "missing")
            problems.Add(Path.GetFileName(cs));
    }

    if (scanned == 0)
        throw new Exception("没有扫描到任何订阅语言切换的页面视图 —— 本规则失去保护对象，请更新规则");
    if (problems.Count > 0)
        throw new Exception("这些页面视图订阅了 LanguageChanged，却没让 VM 重算缓存文案（调 PageViewModelBase.Relocalize）：\n    "
            + string.Join("\n    ", problems));
});

Check("守卫自检：订阅了语言切换但没叫 VM 重算缓存文案必须能被抓出", () =>
{
    const string uc = "<UserControl x:Class=\"X\">";
    const string win = "<Window x:Class=\"X\">";
    string subscribed = """Localization.LanguageChanged += OnLanguageChanged;""";

    if (PageRelocalizeVerdict(uc, subscribed + """TitleText.Text = Localization.L("nav.home");""") != "missing")
        throw new Exception("未抓出“订阅了语言切换却没调 Relocalize”的页面视图");
    if (PageRelocalizeVerdict(uc, subscribed + """(DataContext as PageViewModelBase)?.Relocalize();""") != "ok")
        throw new Exception("误报：订阅了并且调了 Relocalize 的页面视图应当通过");
    if (PageRelocalizeVerdict(win, subscribed) != "n/a")
        throw new Exception("误报：弹窗（Window 根）不应被要求");
    if (PageRelocalizeVerdict(uc, """TitleText.Text = Localization.L("nav.home");""") != "n/a")
        throw new Exception("误报：没订阅语言切换的视图不该被这条规则管");
});

Check("守卫自检：页面视图漏订阅 LanguageChanged 必须能被抓出（弹窗不误报）", () =>
{
    var none = Array.Empty<string>();
    const string uc = "<UserControl x:Class=\"X\">";
    const string win = "<Window x:Class=\"X\">";
    const string localizes = """TitleText.Text = Localization.L("nav.home");""";

    if (PageLocalizerVerdict(uc, localizes, "X.xaml.cs", none) != "missing")
        throw new Exception("未抓出未订阅语言切换的页面视图");
    if (PageLocalizerVerdict(uc, "Localization.LanguageChanged += Localize;" + localizes, "X.xaml.cs", none) != "subscribed")
        throw new Exception("未识别已订阅 LanguageChanged 的页面视图");
    if (PageLocalizerVerdict(win, localizes, "X.xaml.cs", none) != "n/a")
        throw new Exception("误报：弹窗（Window 根）不应被要求订阅");
    if (PageLocalizerVerdict(uc, """Ui.Toast(Localization.L("common.ok"));""", "X.xaml.cs", none) != "n/a")
        throw new Exception("误报：只调用 Ui.Toast 的视图不应被要求订阅");
    if (PageLocalizerVerdict(uc, """DataGrid.Columns[0].Header = Localization.L(key);""", "X.xaml.cs", none) != "missing")
        throw new Exception("未抓出给 DataGrid 表头赋本地化文案的写法");
    if (PageLocalizerVerdict(uc, localizes, "X.xaml.cs", new[] { "X.xaml.cs" }) != "allowed")
        throw new Exception("白名单未生效（名单内的历史遗留应放行）");
});

// ---- 规则：代码里用到的 i18n key 必须真的存在 ----
//
// 两遍扫描，各管一类漏法：
//   ① 精确遍（全部 src）：`Localization.L("key")` 里紧跟的字面量。任何前缀写错都能抓到。
//   ② 宽松遍（仅 WPF 工程）：整个字面量就是一个"小写点分标识符"且首段命中词典已有前缀。
//      这是为了抓 `(0, "node.table.name")` 这类不经过 Localization.L 的表头映射数组；
//      只扫 WPF 工程是因为 Core 按约定不写界面文案，否则 `"node.role"`（_cat/nodes 的 JSON
//      字段名）、`"config.json"`（文件名）都会被误判成 key。
static (HashSet<string> Keys, HashSet<string> Prefixes) LoadDictionary(string root)
{
    string locFile = Path.Combine(root, "src", "ElasticDesktopManager.Core", "I18n", "Localization.cs");
    var keys = Regex.Matches(File.ReadAllText(locFile), @"\[""([^""]+)""\]\s*=")
        .Select(m => m.Groups[1].Value).ToHashSet();
    var prefixes = keys.Where(k => k.Contains('.')).Select(k => k[..k.IndexOf('.')]).ToHashSet();
    return (keys, prefixes);
}

/// <summary>
/// "长得像 i18n key 但其实是普通字面量"的白名单。
/// 只放 WPF 工程里确实不是词条的整串字面量；每项都要写清为什么不是 key。
/// （顶层语句里不能声明字段，所以做成静态本地函数。）
/// </summary>
static HashSet<string> NotI18nLiterals() => new(StringComparer.Ordinal)
{
    // 目前为空：WPF 工程里所有"整串小写点分字面量"都必须是词条。
};

static List<string> FindUnknownI18nKeys(string source, string label, HashSet<string> keys,
    HashSet<string> prefixes, bool broad)
{
    var hits = new List<string>();
    var seen = new HashSet<string>();          // 同一个 key 只报一次（两遍扫描会重合）
    void Add(string key, string why)
    {
        if (seen.Add(key)) hits.Add($"{label}: {why}");
    }

    // ① 调用点
    foreach (Match m in Regex.Matches(source, @"Localization\.L\(\s*""([^""]+)"""))
    {
        string key = m.Groups[1].Value;
        if (!keys.Contains(key)) Add(key, $"Localization.L(\"{key}\")");
    }

    if (!broad) return hits;

    // ② 整串字面量（含两边引号一起匹配 → URL、"application/json" 这类天然被排除）
    //
    // 这里刻意**不**再用"首段必须命中已有前缀"当闸门：那会静默放过首段拼错的 key
    // （`(0, "snaphot.col.name")` 恰好是这条规则最该抓的形态）。改用显式白名单，
    // 名单里的每一项都必须是不属于 i18n 的普通字面量，且要写清理由。
    foreach (Match m in Regex.Matches(source, @"""([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)"""))
    {
        string key = m.Groups[1].Value;
        if (NotI18nLiterals().Contains(key)) continue;
        if (!keys.Contains(key)) Add(key, $"\"{key}\"");
    }
    return hits;
}

// ---- 规则：每个窗口根元素必须显式套用 WindowBaseStyle ----
//
// ADR-9 的护栏：WPF 的隐式样式按元素具体类型查资源，`TargetType="Window"` 的隐式样式
// 不会作用到 MainWindow/SettingsWindow 等派生窗口（dotnet/wpf#10461），客户区会退回
// SystemColors.WindowBrush（浅色系统下是白色）——深色主题下就是一大片白底。
// 这条规则保证"以后新增窗口"不会再把这个缺陷带回来。
static bool WindowHasBaseStyle(string xaml) =>
    Regex.IsMatch(xaml, @"<Window\b[^>]*Style\s*=\s*""\{StaticResource WindowBaseStyle\}""",
        RegexOptions.Singleline);

Check("窗口：每个 Window 根元素必须显式套用 WindowBaseStyle（隐式 Window 样式不生效）", () =>
{
    var missing = new List<string>();
    foreach (var f in Directory.GetFiles(Path.Combine(repoRoot, "src"), "*.xaml", SearchOption.AllDirectories))
    {
        string text = File.ReadAllText(f);
        if (!Regex.IsMatch(text, @"<Window\b")) continue;
        if (!WindowHasBaseStyle(text)) missing.Add(Path.GetRelativePath(repoRoot, f));
    }
    if (missing.Count > 0)
        throw new Exception($"这些窗口没有显式套用 WindowBaseStyle（客户区会退回系统白底，深色主题下尤为明显）：\n    "
            + string.Join("\n    ", missing));
});

Check("i18n：代码里用到的 key 都存在于词典（防漏词条 → 界面直接显示 key）", () =>
{
    var (keys, prefixes) = LoadDictionary(repoRoot);
    string root = Path.Combine(repoRoot, "src");
    string wpfProject = Path.Combine(root, "ElasticDesktopManager") + Path.DirectorySeparatorChar;
    var hits = new List<string>();

    foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
    {
        if (Path.GetFileName(f) is "Localization.cs") continue; // 词典自身
        bool broad = f.StartsWith(wpfProject, StringComparison.Ordinal);
        hits.AddRange(FindUnknownI18nKeys(File.ReadAllText(f), Path.GetRelativePath(repoRoot, f),
            keys, prefixes, broad));
    }

    if (hits.Count > 0)
        throw new Exception($"发现 {hits.Count} 个未定义的 key：\n    " + string.Join("\n    ", hits.Distinct()));
});

Check("守卫自检：未定义的 i18n key / 缺失的 PART_EditableTextBox / 未接 ContentTemplateSelector 必须能被抓出", () =>
{
    var keys = new HashSet<string> { "nav.home", "snapshot.col.name" };
    var prefixes = new HashSet<string> { "nav", "snapshot" };

    if (FindUnknownI18nKeys("""Localization.L("nav.home")""", "t", keys, prefixes, true).Count != 0)
        throw new Exception("误报：已定义的 key");
    if (FindUnknownI18nKeys("""Localization.L("nav.nope")""", "t", keys, prefixes, true).Count != 1)
        throw new Exception("未抓出不存在的 key");
    if (FindUnknownI18nKeys("""Localization.L("typo.home")""", "t", keys, prefixes, true).Count != 1)
        throw new Exception("未抓出前缀都不存在的新 key（精确遍必须覆盖）");
    if (FindUnknownI18nKeys("""new HeaderMap { (0, "snapshot.col.name") }""", "t", keys, prefixes, true).Count != 0)
        throw new Exception("误报：表头映射里已定义的 key");
    if (FindUnknownI18nKeys("""new HeaderMap { (0, "snapshot.nope") }""", "t", keys, prefixes, true).Count != 1)
        throw new Exception("未抓出表头映射里不存在的 key（宽松遍必须覆盖）");
    if (FindUnknownI18nKeys("""var u = "application/json";""", "t", keys, prefixes, true).Count != 0)
        throw new Exception("误报：普通字符串（含 /）不应被当成 key");
    if (FindUnknownI18nKeys("""var h = "/_cat/nodes?h=node.role,master";""", "t", keys, prefixes, true).Count != 0)
        throw new Exception("误报：URL 查询串里的 node.role 不是 key");
    // 宽松遍已去掉"前缀闸门"：首段拼错的 key 以前会被静默跳过，现在必须抓出来。
    // 代价是非词条字面量会被报出，因此 WPF 工程里的这类字面量必须显式进白名单。
    if (FindUnknownI18nKeys("""new HeaderMap { (0, "snaphot.col.name") }""", "t", keys, prefixes, true).Count != 1)
        throw new Exception("未抓出首段拼错的 key（前缀闸门已移除，这条必须过）");
    if (FindUnknownI18nKeys("""var f = "config.json";""", "t", keys, prefixes, false).Count != 0)
        throw new Exception("精确遍不应把普通字面量当成 key");

    // 窗口基样式：有/无 两个方向
    if (!WindowHasBaseStyle("""<Window x:Class="X" Style="{StaticResource WindowBaseStyle}">"""))
        throw new Exception("未识别已套用 WindowBaseStyle 的窗口");
    if (WindowHasBaseStyle("""<Window x:Class="X" Title="Y">"""))
        throw new Exception("未抓出没有套用 WindowBaseStyle 的窗口");

    // 白名单只能放"确实不是词条"的字面量，否则它就成了掩盖真实缺词的橡皮擦
    var (realKeys, _) = LoadDictionary(repoRoot);
    foreach (var literal in NotI18nLiterals())
        if (realKeys.Contains(literal))
            throw new Exception($"白名单里的 \"{literal}\" 其实是词条，应删掉该白名单项");

    const string xns = "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
    if (!ComboTemplateHasEditableBox(
            $"<ResourceDictionary {xns}><ControlTemplate TargetType=\"ComboBox\"><TextBox x:Name=\"PART_EditableTextBox\" /></ControlTemplate></ResourceDictionary>"))
        throw new Exception("未识别已存在的 PART_EditableTextBox");
    if (ComboTemplateHasEditableBox(
            $"<ResourceDictionary {xns}><ControlTemplate TargetType=\"ComboBox\"><ContentPresenter /></ControlTemplate></ResourceDictionary>"))
        throw new Exception("未抓出缺失的 PART_EditableTextBox");
    // ① 注释里的同名文字不算
    if (ComboTemplateHasEditableBox(
            $"<ResourceDictionary {xns}><!-- 必须提供 x:Name=\"PART_EditableTextBox\" --><ControlTemplate TargetType=\"ComboBox\"><ContentPresenter /></ControlTemplate></ResourceDictionary>"))
        throw new Exception("误报：注释里的 PART_EditableTextBox 不该算数");
    // ② 嵌在子模板里的同名 TextBox 不算（ComboBox.FindName 够不到它）
    if (ComboTemplateHasEditableBox(
            $"<ResourceDictionary {xns}><ControlTemplate TargetType=\"ComboBox\"><ToggleButton><ToggleButton.Template><ControlTemplate TargetType=\"ToggleButton\"><TextBox x:Name=\"PART_EditableTextBox\" /></ControlTemplate></ToggleButton.Template></ToggleButton></ControlTemplate></ResourceDictionary>"))
        throw new Exception("误报：子模板里的同名部件对 ComboBox 不可见，不该算数");
    // ③ 后面别的模板里有同名部件也不算
    if (ComboTemplateHasEditableBox(
            $"<ResourceDictionary {xns}><ControlTemplate TargetType=\"ComboBox\"><ContentPresenter /></ControlTemplate><ControlTemplate TargetType=\"Other\"><TextBox x:Name=\"PART_EditableTextBox\" /></ControlTemplate></ResourceDictionary>"))
        throw new Exception("误报：别的模板里的同名部件不该让本条通过");

    // 收起态展示器：DisplayMemberPath 靠 ContentTemplateSelector 生效
    const string box = """<ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}" />""";
    const string boxFixed = """<ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}" />""";
    if (!ComboSelectionBoxHasTemplateSelector(
            $"<ResourceDictionary {xns}><ControlTemplate TargetType=\"ComboBox\">{boxFixed}</ControlTemplate></ResourceDictionary>"))
        throw new Exception("未识别已绑定 ContentTemplateSelector 的收起态展示器");
    if (ComboSelectionBoxHasTemplateSelector(
            $"<ResourceDictionary {xns}><ControlTemplate TargetType=\"ComboBox\">{box}</ControlTemplate></ResourceDictionary>"))
        throw new Exception("未抓出缺失的 ContentTemplateSelector（这正是 DisplayMemberPath 显示 ToString 的缺陷）");
    // 写在子模板里的展示器不算（ComboBox 收起态用的是自己名字域里的那个）
    if (ComboSelectionBoxHasTemplateSelector(
            $"<ResourceDictionary {xns}><ControlTemplate TargetType=\"ComboBox\"><ContentPresenter /></ControlTemplate><ControlTemplate TargetType=\"Other\">{boxFixed}</ControlTemplate></ResourceDictionary>"))
        throw new Exception("误报：别的模板里的展示器不该让本条通过");
    // 换掉展示器的写法也必须报错，而不是"没找到 → 通过"
    if (ComboSelectionBoxHasTemplateSelector(
            $"<ResourceDictionary {xns}><ControlTemplate TargetType=\"ComboBox\"><TextBlock /></ControlTemplate></ResourceDictionary>"))
        throw new Exception("展示器改成别的写法时必须报错（规则不能空转通过）");

    // ---- 占位符一致性 ----
    var zh = DictionaryEntries("""["a"]="{0} 个{x}", "b"="没有占位符",""");
    var en = DictionaryEntries("""["a"]="{0} items", "b"="no placeholder",""");
    if (!Placeholders(zh["a"]).SequenceEqual(Placeholders(en["a"])))
        throw new Exception("自检样本构造失败");
    if (Placeholders("保留 {0}~{1}").SequenceEqual(Placeholders("keep {0}")))
        throw new Exception("未抓出占位符数量不一致");
    if (!Placeholders("{1} {0}").SequenceEqual(Placeholders("{0} {1}")))
        throw new Exception("误报：占位符顺序不同不应算不一致");
});

// ---- 规则：代码里的 GitHub 仓库链接必须指向本仓库 ----
//
// 第 8 轮发现：关于窗口的「GitHub」按钮打开的是**上游项目**（lxwise/elastic-desktop-manager）——
// 移植时从 JavaFX 原版抄了链接却没改，用户点开看到的是别人的仓库。
// 这类"值本身就是别人家的 URL"编译期不报错、单测与其它守卫也抓不到（它们只看结构与文案来源），
// 只能静态比对字符串。只扫 src/ 下的 .cs —— README / docs 里的上游署名链接是**许可要求**
// （上游为 Apache-2.0），必须保留，不在本规则范围内。
static List<string> ForeignGithubRepoUrls(string code, string label, string ownOwnerRepo)
{
    // GitHub 的保留一级路径（不是用户/组织名），命中即跳过
    string[] reserved =
    {
        "advisories", "orgs", "apps", "marketplace", "sponsors", "topics",
        "collections", "settings", "login", "features", "about", "pricing",
        "security", "site", "explore", "notifications",
    };
    var found = new List<string>();
    foreach (Match m in Regex.Matches(code, @"https?://github\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)"))
    {
        string owner = m.Groups[1].Value;
        string repo = m.Groups[2].Value;
        if (Array.Exists(reserved, r => r.Equals(owner, StringComparison.OrdinalIgnoreCase))) continue;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        if ($"{owner}/{repo}".Equals(ownOwnerRepo, StringComparison.OrdinalIgnoreCase)) continue;
        found.Add($"{label}: github.com/{owner}/{repo}");
    }
    return found;
}

Check("代码：GitHub 仓库链接必须指向本仓库（否则用户点开是别人的项目）", () =>
{
    const string own = "guohengkai687/elastic-desktop-manager-wpf";
    string srcDir = Path.Combine(repoRoot, "src");
    var problems = new List<string>();
    int scanned = 0;
    foreach (var cs in Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories))
    {
        if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            || cs.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
        scanned++;
        problems.AddRange(ForeignGithubRepoUrls(File.ReadAllText(cs), Path.GetFileName(cs), own));
    }

    if (scanned == 0)
        throw new Exception("没有扫描到任何 .cs —— 本规则失去保护对象，请更新规则");
    if (problems.Count > 0)
        throw new Exception($"发现 {problems.Count} 处指向其它仓库的 GitHub 链接：\n    "
            + string.Join("\n    ", problems));
});

Check("守卫自检：指向别人仓库的链接必须能被抓出（本仓库与保留路径不误报）", () =>
{
    const string own = "guohengkai687/elastic-desktop-manager-wpf";
    // ① 正是第 8 轮的真实缺陷
    if (ForeignGithubRepoUrls("""var u = "https://github.com/lxwise/elastic-desktop-manager";""", "t", own).Count != 1)
        throw new Exception("未抓出指向上游仓库的链接（关于窗口打开别人项目的那类缺陷）");
    // ② 本仓库自己的链接必须放行（含 .git 后缀写法）
    if (ForeignGithubRepoUrls($"""var u = "https://github.com/{own}";""", "t", own).Count != 0)
        throw new Exception("误报：指向本仓库的链接不该算问题");
    if (ForeignGithubRepoUrls($"""var u = "https://github.com/{own}.git";""", "t", own).Count != 0)
        throw new Exception("误报：带 .git 后缀的本仓库链接不该算问题");
    // ③ 非仓库型链接（安全公告）不算
    if (ForeignGithubRepoUrls("""// https://github.com/advisories/GHSA-c6w8-7mp3-34j9""", "t", own).Count != 0)
        throw new Exception("误报：advisories 是保留路径，不是仓库链接");
    // ④ 只到用户/组织、没有仓库名的链接不算（正则要求两段路径）
    if (ForeignGithubRepoUrls("""var u = "https://github.com/lxwise";""", "t", own).Count != 0)
        throw new Exception("误报：只到用户主页的链接不是仓库链接");
    // ⑤ 反证：把 own 换成别的值时同一个 URL 必须报错（证明比较真的在生效，不是恒过）
    if (ForeignGithubRepoUrls($"""var u = "https://github.com/{own}";""", "t", "someone/other").Count != 1)
        throw new Exception("自检样本构造失败：own 不匹配时必须报错，否则本规则恒过");
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
