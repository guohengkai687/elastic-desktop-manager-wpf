using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

/// <summary>
/// ES 查询示例窗口：按分类展示内置常用查询，预览后将 method/path/body 回填到 REST 页。
/// </summary>
public partial class EsExamplesWindow : Window
{
    private const string CategoryTag = "category";

    private readonly RestViewModel? _restVm;
    private EsQueryExample? _selected;
    private bool _suppressFilter;

    public EsExamplesWindow()
    {
        InitializeComponent();
        Owner = Ui.Main;
        _restVm = (Ui.Main?.DataContext as MainViewModel)?.GetRestViewModel();

        Title = Localization.L("rest.examples.title");
        TipText.Text = Localization.L("rest.examples.tip");
        IndexLabel.Text = Localization.L("rest.examples.indexLabel");
        ApplyButton.Content = Localization.L("rest.examples.apply");
        CloseButton.Content = Localization.L("common.close");

        FilterBox.Text = Localization.L("rest.examples.search");
        FilterBox.Foreground = (System.Windows.Media.Brush)FindResource("DimTextBrush");
        FilterBox.GotFocus += OnFilterGotFocus;
        FilterBox.LostFocus += OnFilterLostFocus;
        FilterBox.TextChanged += (_, _) => RebuildTree();
        IndexBox.TextChanged += (_, _) => RenderPreview();

        BuildTree();
        Loaded += (_, _) => ExampleTree.Focus();
    }

    // ---------- 占位提示 ----------
    private void OnFilterGotFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressFilter) return;
        FilterBox.Text = "";
        FilterBox.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
    }

    private void OnFilterLostFocus(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(FilterBox.Text)) return;
        _suppressFilter = true;
        FilterBox.Text = Localization.L("rest.examples.search");
        FilterBox.Foreground = (System.Windows.Media.Brush)FindResource("DimTextBrush");
        _suppressFilter = false;
    }

    private string CurrentFilter()
    {
        var text = FilterBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return "";
        // 占位符文本不算筛选条件
        if (text == Localization.L("rest.examples.search")) return "";
        return text.Trim();
    }

    // ---------- 树构建 ----------
    private void BuildTree()
    {
        RebuildTree();
    }

    private void RebuildTree()
    {
        var filter = CurrentFilter();
        var root = new List<TreeItem>();

        foreach (var category in EsQueryExampleCatalog.Categories)
        {
            var examples = EsQueryExampleCatalog.ByCategory(category)
                .Select(ex => new
                {
                    Example = ex,
                    Title = Localization.L(ex.TitleKey),
                    Desc = Localization.L(ex.DescKey),
                })
                .Where(x => Matches(x.Title, x.Desc, x.Example, filter))
                .ToList();

            if (examples.Count == 0) continue;

            var categoryNode = new TreeItem
            {
                Title = Localization.L($"rest.example.cat.{category}"),
                IsCategory = true,
            };

            categoryNode.Children.AddRange(examples.Select(x => new TreeItem
            {
                Title = x.Title,
                Example = x.Example,
            }));

            root.Add(categoryNode);
        }

        ExampleTree.ItemsSource = root;
        EmptyHint.Text = Localization.L("rest.examples.empty");
        EmptyHint.Visibility = root.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 自动选中第一条示例，保证预览区不空
        var first = root.SelectMany(r => r.Children).FirstOrDefault();
        if (first is not null)
        {
            first.IsSelected = true;
            _selected = first.Example;
            RenderPreview();
        }
        else
        {
            _selected = null;
            RenderPreview();
        }
    }

    private static bool Matches(string title, string desc, EsQueryExample ex, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        var haystack = $"{title} {desc} {ex.Method} {ex.Path} {ex.Body} {ex.Category}";
        return haystack.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>树节点：分类（无 Example）或示例叶子。</summary>
    public sealed class TreeItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Title { get; init; } = "";
        public bool IsCategory { get; init; }
        public EsQueryExample? Example { get; init; }
        public List<TreeItem> Children { get; } = new();

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>让 TreeView 知道分类节点展开、叶子节点可选中。</summary>
    private void OnExampleSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeItem { Example: not null } item)
        {
            _selected = item.Example;
            ApplyButton.IsEnabled = true;
            RenderPreview();
        }
        else
        {
            _selected = null;
            ApplyButton.IsEnabled = false;
            RenderPreview();
        }
    }

    // ---------- 预览 ----------
    private void RenderPreview()
    {
        if (_selected is null)
        {
            PreviewTitle.Text = "";
            PreviewDesc.Text = "";
            MethodValue.Text = "";
            PathBox.Text = "";
            BodyBox.Text = "";
            BodyEmptyHint.Visibility = Visibility.Collapsed;
            return;
        }

        var (method, path, body) = EsQueryExampleCatalog.Materialize(_selected, IndexBox.Text);

        PreviewTitle.Text = Localization.L(_selected.TitleKey);
        PreviewDesc.Text = Localization.L(_selected.DescKey);
        MethodValue.Text = method;
        PathBox.Text = path;
        BodyBox.Text = body;

        if (string.IsNullOrWhiteSpace(body))
        {
            BodyBox.Visibility = Visibility.Collapsed;
            BodyEmptyHint.Text = Localization.L("rest.examples.method") + ": " + method;
            BodyEmptyHint.Visibility = Visibility.Visible;
        }
        else
        {
            BodyBox.Visibility = Visibility.Visible;
            BodyEmptyHint.Visibility = Visibility.Collapsed;
        }
    }

    // ---------- 动作 ----------
    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _restVm is null) return;

        var (method, path, body) = EsQueryExampleCatalog.Materialize(_selected, IndexBox.Text);
        _restVm.LoadFromExample(method, path, body);

        Ui.Toast(Localization.L("rest.examples.applied", Localization.L(_selected.TitleKey)));
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
