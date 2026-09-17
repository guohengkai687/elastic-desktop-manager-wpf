// WPF 项目全局 using（net8.0-windows 隐式 using 不含这些）
global using System.IO;
global using System.Threading.Tasks;
global using System.Windows.Input;
global using System.Collections.ObjectModel;
global using System.Collections.Specialized;

// 消除与 System.Windows.Localization 的歧义
global using Localization = ElasticDesktopManager.Core.I18n.Localization;