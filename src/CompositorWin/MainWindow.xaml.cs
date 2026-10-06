using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CompositorWin.Core;
using Microsoft.Win32;
using SkiaSharp;
using Path = System.IO.Path;

namespace CompositorWin;

public enum EditorTool { Move, Marquee, Lasso, Wand, Crop, Brush, Heal, Clone, Blur, Gradient, Shape, Type, Eyedropper, Hand, Zoom, Mask, Eraser }

public partial class MainWindow : Window
{
    private readonly List<CompDocument> _docs = [];
    private CompDocument? _document;
    private readonly CompWatcher _watcher;
    private LiveServer? _server;
    private bool _dirtyFromUi;
    private bool _updatingUi;
    private double _zoom = 1;
    private EditorTool _tool = EditorTool.Move;
    private bool _down;
    private System.Windows.Point _start;
    private System.Windows.Point _last;
    private SKPoint? _cloneSource;
    private readonly List<SKPoint> _stroke = [];
    private SKBitmap? _selection;
    private bool _showsRulers;
    private bool _splitterDown;
    private double _splitterStart;
    private double _layersStart = 252;
    private SKColor _fg = new(45, 91, 255);
    private SKColor _bg = SKColors.White;
    private double _scrollX;
    private double _scrollY;
    private System.Windows.Point _panStart;
    private SKBitmap? _workPixels;
    private bool _renderQueued;
    private readonly DispatcherTimer _opacityTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private WriteableBitmap? _canvasWb;
    public ObservableCollection<LayerRow> Layers { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        LayerList.ItemsSource = Layers;
        foreach (var name in BlendModeNames.All)
        {
            BlendCombo.Items.Add(new ComboBoxItem { Content = BlendLabel(name), Tag = name });
        }

        SelectBlend("Normal");
        BlendCombo.SelectionChanged += Blend_Changed;
        foreach (var family in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
        {
            FontCombo.Items.Add(family.Source);
        }

        if (FontCombo.Items.Count > 0) FontCombo.SelectedIndex = 0;
        _opacityTimer.Tick += (_, _) =>
        {
            _opacityTimer.Stop();
            RenderCanvas();
        };
        foreach (var child in ToolBar.Children)
        {
            if (child is Button btn)
            {
                btn.MouseEnter += Tool_MouseEnter;
            }
        }

        HighlightTool();
        UpdateChrome();
        _watcher = new CompWatcher(path => Dispatcher.Invoke(() => ReloadFromDisk(path, "已从 Agent 更新")));
        try
        {
            _server = new LiveServer((path, open) => Dispatcher.Invoke(() =>
            {
                if (open || _document is null || !SamePath(_document.PackagePath, path))
                {
                    LoadPackage(path, fromAgent: true);
                }
                else
                {
                    ReloadFromDisk(path, "已从 Agent 更新");
                }
            }));
            _server.Start();
        }
        catch (Exception ex)
        {
            Status("实时端口未启动：" + ex.Message);
        }

        Loaded += (_, _) =>
        {
            if (App.LaunchArgs.Length > 0 && Directory.Exists(App.LaunchArgs[0]))
            {
                LoadPackage(App.LaunchArgs[0], fromAgent: true);
            }
        };
        Closed += (_, _) =>
        {
            _server?.Dispose();
            _watcher.Dispose();
            _selection?.Dispose();
            _workPixels?.Dispose();
            _opacityTimer.Stop();
        };
        HighlightTool();
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && Enum.TryParse<EditorTool>(b.Tag?.ToString(), out var t))
        {
            _tool = t;
            HighlightTool();
            UpdateChrome();
            Status(HintFor(_tool));
        }
    }

    private void Tool_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button b && Enum.TryParse<EditorTool>(b.Tag?.ToString(), out var t))
        {
            Status(HintFor(t));
        }
        else if (sender is FrameworkElement fe && fe.ToolTip is string tip)
        {
            Status(tip);
        }
    }

    private void HighlightTool()
    {
        var selectedBg = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255));
        var selectedBd = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255));
        foreach (var child in ToolBar.Children)
        {
            if (child is Button btn && btn.Tag is string tag)
            {
                var on = tag == _tool.ToString() || (_tool == EditorTool.Eraser && tag == "Brush");
                btn.Background = on ? selectedBg : Brushes.Transparent;
                btn.BorderBrush = on ? selectedBd : Brushes.Transparent;
                btn.BorderThickness = new Thickness(on ? 1 : 0);
            }
        }
    }

    private void UpdateChrome()
    {
        HeaderMove.Visibility = _tool == EditorTool.Move ? Visibility.Visible : Visibility.Collapsed;
        HeaderBrush.Visibility = _tool is EditorTool.Brush or EditorTool.Eraser or EditorTool.Clone or EditorTool.Heal or EditorTool.Blur or EditorTool.Mask ? Visibility.Visible : Visibility.Collapsed;
        HeaderSelect.Visibility = _tool is EditorTool.Marquee or EditorTool.Lasso or EditorTool.Wand ? Visibility.Visible : Visibility.Collapsed;
        HeaderType.Visibility = _tool == EditorTool.Type ? Visibility.Visible : Visibility.Collapsed;
        HeaderCrop.Visibility = _tool == EditorTool.Crop ? Visibility.Visible : Visibility.Collapsed;
        HeaderNav.Visibility = _tool is EditorTool.Hand or EditorTool.Zoom ? Visibility.Visible : Visibility.Collapsed;
        HeaderIdle.Visibility = Visibility.Collapsed;
        BrushTitle.Text = _tool switch
        {
            EditorTool.Eraser => "橡皮擦",
            EditorTool.Clone => "仿制图章",
            EditorTool.Heal => "污点修复画笔",
            EditorTool.Blur => "涂抹",
            EditorTool.Mask => "蒙版",
            _ => "画笔"
        };
        SelectTitle.Text = _tool switch
        {
            EditorTool.Lasso => "套索",
            EditorTool.Wand => "魔棒",
            _ => "矩形选框"
        };
        NavTitle.Text = _tool == EditorTool.Zoom ? "缩放" : "抓手";
        Welcome.Visibility = _document is null ? Visibility.Visible : Visibility.Collapsed;
        ZoomStatus.Text = _document is null ? "—" : $"{_zoom * 100:0.#}%";
        SizeStatus.Text = _document is null ? "" : $"{_document.Manifest.Width} × {_document.Manifest.Height} 像素";
        StatusText.Text = _document is null ? "准备就绪" : HintFor(_tool);
        var vis = _showsRulers && _document is not null ? Visibility.Visible : Visibility.Collapsed;
        RulerH.Visibility = vis;
        RulerV.Visibility = vis;
        RulerCorner.Visibility = vis;
    }

    private static string HintFor(EditorTool t) => t switch
    {
        EditorTool.Marquee => "拖出矩形选区 · Shift 相加 · Alt 相减 · Delete 清除 · Ctrl+D 取消选择",
        EditorTool.Wand => "点击选取相近颜色 · Ctrl+D 取消选择",
        EditorTool.Lasso => "拖动建立选区 · Delete 清除 · Ctrl+D 取消选择",
        EditorTool.Brush => "拖动涂抹 · [ ] 调整大小 · 1–0 不透明度 · 空格平移",
        EditorTool.Eraser => "拖动擦除 · [ ] 调整大小 · 空格平移",
        EditorTool.Clone => "Alt+点击设置源 · 拖动仿制",
        EditorTool.Heal => "在瑕疵上拖动以修复",
        EditorTool.Type => "点击添加文本框 · Esc 取消",
        EditorTool.Shape => "拖动在新图层上绘制形状",
        EditorTool.Gradient => "拖动绘制渐变",
        EditorTool.Crop => "拖动裁剪 · Enter 应用 · Esc 取消",
        EditorTool.Move => "拖动移动 · 1–0 图层不透明度 · 空格平移",
        EditorTool.Hand => "拖动平移 · Ctrl+滚轮缩放",
        EditorTool.Zoom => "单击放大 · Alt+单击缩小",
        EditorTool.Eyedropper => "在画布上点击取样颜色",
        _ => "按工具快捷键切换 · 空格平移"
    };

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Deselect_Click(sender, e);
            return;
        }

        if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Rulers_Click(sender, e);
            return;
        }

        if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control) { Fit_Click(sender, e); return; }
        if (e.Key == Key.D1 && Keyboard.Modifiers == ModifierKeys.Control) { Actual_Click(sender, e); return; }

        _tool = e.Key switch
        {
            Key.V => EditorTool.Move,
            Key.C => EditorTool.Crop,
            Key.B => EditorTool.Brush,
            Key.E => EditorTool.Eraser,
            Key.S => EditorTool.Clone,
            Key.G => EditorTool.Gradient,
            Key.U => EditorTool.Shape,
            Key.T => EditorTool.Type,
            Key.M => EditorTool.Marquee,
            Key.L => EditorTool.Lasso,
            Key.W => EditorTool.Wand,
            Key.J => EditorTool.Heal,
            Key.R => EditorTool.Blur,
            Key.I => EditorTool.Eyedropper,
            Key.H => EditorTool.Hand,
            Key.Z => EditorTool.Zoom,
            _ => _tool
        };
        HighlightTool();
        UpdateChrome();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var w = int.TryParse(NewW.Text, out var ww) ? Math.Clamp(ww, 16, 16384) : 1920;
        var h = int.TryParse(NewH.Text, out var hh) ? Math.Clamp(hh, 16, 16384) : 1080;
        var dialog = new SaveFileDialog { Filter = "Compositor 项目|*.comp", FileName = "未命名.comp" };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FileName.EndsWith(".comp", StringComparison.OrdinalIgnoreCase) ? dialog.FileName : dialog.FileName + ".comp";
        Directory.CreateDirectory(path);
        Adopt(ProjectStore.CreateNew(path, w, h, new SKColor(255, 255, 255)));
    }

    private void WelcomeNew_Click(object sender, RoutedEventArgs e) => New_Click(sender, e);

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "打开 .comp 项目文件夹" };
        if (dialog.ShowDialog() == true) LoadPackage(dialog.FolderName, false);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        ProjectStore.Save(_document);
        _dirtyFromUi = false;
        _watcher.RememberFingerprint();
        Status("已保存");
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "图像|*.png;*.jpg;*.jpeg;*.webp;*.tif;*.tiff;*.bmp;*.psd" };
        if (dialog.ShowDialog() != true) return;
        if (dialog.FileName.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || dialog.FileName.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
        {
            ImportPsdFile(dialog.FileName);
            return;
        }

        EnsureDocFromImage(dialog.FileName);
        ProjectStore.AddImageLayer(_document!, dialog.FileName);
        BindDocument();
        Status("已导入");
    }

    private void ImportPsd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PSD 文件|*.psd;*.psb" };
        if (dialog.ShowDialog() == true) ImportPsdFile(dialog.FileName);
    }

    private void ImportPsdFile(string path)
    {
        EnsureDocFromImage(path);
        PsdImport.ImportFlattened(_document!, path);
        BindDocument();
        Status("已导入 PSD 拼合图层");
    }

    private void EnsureDocFromImage(string imagePath)
    {
        if (_document is not null) return;
        var folder = Path.Combine(Path.GetDirectoryName(imagePath)!, Path.GetFileNameWithoutExtension(imagePath) + ".comp");
        using var probe = imagePath.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) ? null : ImageIO.LoadRgba(imagePath);
        _document = ProjectStore.CreateNew(folder, probe?.Width ?? 1920, probe?.Height ?? 1080);
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e) => Export("png");
    private void ExportJpg_Click(object sender, RoutedEventArgs e) => Export("jpg");

    private void Export(string format)
    {
        if (_document is null) return;
        var dialog = new SaveFileDialog { Filter = format == "png" ? "PNG|*.png" : "JPEG|*.jpg" };
        if (dialog.ShowDialog() != true) return;
        DocumentService.ExportFlattened(_document.PackagePath, dialog.FileName, format);
        Status("已导出");
    }

    private void HueSat_Click(object s, RoutedEventArgs e) => AddAdj(new AdjustmentRecord { Kind = "Hue/Saturation", Saturation = 10 }, "色相/饱和度");
    private void Levels_Click(object s, RoutedEventArgs e) => AddAdj(new AdjustmentRecord { Kind = "Levels" }, "色阶");
    private void Curves_Click(object s, RoutedEventArgs e) => AddAdj(new AdjustmentRecord { Kind = "Curves" }, "曲线");
    private void Exposure_Click(object s, RoutedEventArgs e) => AddAdj(new AdjustmentRecord { Kind = "Exposure", Exposure = new ExposureSettings { Exposure = 0.25 } }, "曝光");
    private void Bw_Click(object s, RoutedEventArgs e) => AddAdj(new AdjustmentRecord { Kind = "Black & White" }, "黑白");
    private void Balance_Click(object s, RoutedEventArgs e) => AddAdj(new AdjustmentRecord { Kind = "Color Balance" }, "色彩平衡");
    private void Invert_Click(object s, RoutedEventArgs e) => AddAdj(new AdjustmentRecord { Kind = "Invert" }, "反相");
    private void Raw_Click(object s, RoutedEventArgs e)
    {
        if (_document is null || Active() is null) return;
        Active()!.CameraRaw = new CameraRawRecord { Contrast = 20, Vibrance = 15, Shadows = 20 };
        ProjectStore.Save(_document);
        BindDocument();
    }

    private void AddAdj(AdjustmentRecord adj, string name)
    {
        if (_document is null) return;
        ProjectStore.AddAdjustmentLayer(_document, adj, name);
        BindDocument();
    }

    private void Group_Click(object s, RoutedEventArgs e)
    {
        if (_document is null) return;
        ProjectStore.AddGroup(_document);
        BindDocument();
    }

    private void Clip_Click(object s, RoutedEventArgs e)
    {
        if (_document is null || Active() is null) return;
        var i = _document.Manifest.Layers.FindIndex(l => l.Id == Active()!.Id);
        if (i <= 0) return;
        Active()!.MaskSourceId = _document.Manifest.Layers[i - 1].Id;
        ProjectStore.Save(_document);
        BindDocument();
        Status("已设剪贴蒙版");
    }

    private void RemoveBg_Click(object s, RoutedEventArgs e) => MutatePixels(b => { using var r = PixelTools.RemoveBackground(b); r.CopyTo(b); });
    private void Subject_Click(object s, RoutedEventArgs e)
    {
        if (_document is null || Active() is null) return;
        using var px = ProjectStore.LoadLayerPixels(_document, Active()!);
        using var mask = PixelTools.SubjectMask(px);
        ProjectStore.SetLayerMask(_document, Active()!.Id, mask);
        BindDocument();
    }

    private void Fill_Click(object s, RoutedEventArgs e)
    {
        if (_selection is null) { Status("先做选区"); return; }
        MutatePixels(b => PixelTools.ContentAwareFill(b, _selection));
    }

    private void Shadow_Click(object s, RoutedEventArgs e) => SetEffect(fx => fx.Shadow = new EffectShadow());
    private void Stroke_Click(object s, RoutedEventArgs e) => SetEffect(fx => fx.Stroke = new EffectStroke());
    private void Overlay_Click(object s, RoutedEventArgs e) => SetEffect(fx => fx.ColorOverlay = new EffectOverlay { Opacity = 0.25, Red = 1, Green = 0.85, Blue = 0.4 });

    private void SetEffect(Action<EffectsRecord> edit)
    {
        if (Active() is null || _document is null) return;
        Active()!.Effects ??= new EffectsRecord();
        edit(Active()!.Effects!);
        ProjectStore.Save(_document);
        BindDocument();
    }

    private void CanvasSize_Click(object s, RoutedEventArgs e)
    {
        if (_document is null) return;
        CanvasOps.ResizeCanvas(_document, _document.Manifest.Width + 120, _document.Manifest.Height + 120);
        BindDocument();
    }

    private void ImageSize_Click(object s, RoutedEventArgs e)
    {
        if (_document is null) return;
        CanvasOps.ResizeImage(_document, Math.Max(32, _document.Manifest.Width / 2), Math.Max(32, _document.Manifest.Height / 2));
        BindDocument();
    }

    private void Crop_Click(object s, RoutedEventArgs e)
    {
        if (_document is null || _selection is null) return;
        var bounds = Bounds(_selection);
        CanvasOps.Crop(_document, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        _selection.Dispose();
        _selection = null;
        BindDocument();
    }

    private void GuideV_Click(object s, RoutedEventArgs e) => AddGuide("vertical");
    private void GuideH_Click(object s, RoutedEventArgs e) => AddGuide("horizontal");

    private void AddGuide(string axis)
    {
        if (_document is null) return;
        _document.Manifest.Guides ??= [];
        _document.Manifest.Guides.Add(new GuideRecord { Axis = axis, Position = axis == "vertical" ? _document.Manifest.Width / 2.0 : _document.Manifest.Height / 2.0 });
        ProjectStore.Save(_document);
        DrawOverlay();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Adopt(CompDocument doc)
    {
        _document = doc;
        if (_docs.All(d => !SamePath(d.PackagePath, doc.PackagePath)))
        {
            _docs.Add(doc);
        }

        RefreshTabs();
        BindDocument();
    }

    private void RefreshTabs()
    {
        TabStrip.Children.Clear();
        foreach (var d in _docs)
        {
            var on = _document is not null && SamePath(_document.PackagePath, d.PackagePath);
            var btn = new Button
            {
                Content = Path.GetFileName(d.PackagePath.TrimEnd('\\')),
                Tag = d,
                Style = (Style)FindResource("TabPill"),
                Background = on ? (Brush)FindResource("PillActive") : Brushes.Transparent,
                FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal
            };
            btn.Click += (_, _) =>
            {
                if (btn.Tag is CompDocument doc)
                {
                    _document = doc;
                    BindDocument();
                }
            };
            TabStrip.Children.Add(btn);
        }
    }

    private void LoadPackage(string path, bool fromAgent)
    {
        try
        {
            if (_dirtyFromUi && !fromAgent && MessageBox.Show(this, "有未保存修改，打开磁盘版本？", "CompositorWin", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            {
                return;
            }

            Adopt(ProjectStore.Load(path));
            _dirtyFromUi = false;
            Status(fromAgent ? "已从 Agent 打开" : "已打开");
        }
        catch (Exception ex)
        {
            Status("打开失败：" + ex.Message);
        }
    }

    private void ReloadFromDisk(string path, string message)
    {
        if (_document is null || !SamePath(_document.PackagePath, path))
        {
            LoadPackage(path, true);
            return;
        }

        if (_dirtyFromUi && MessageBox.Show(this, "Agent 写入了新版本，用 Agent 版本？", "CompositorWin", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var z = _zoom;
            _document = ProjectStore.Load(path);
            var i = _docs.FindIndex(d => SamePath(d.PackagePath, path));
            if (i >= 0) _docs[i] = _document;
            _dirtyFromUi = false;
            BindDocument();
            _zoom = z;
            ApplyZoom();
            Status(message + "  " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch { }
    }

    private void BindDocument()
    {
        if (_document is null)
        {
            Title = "未命名 — CompositorWin";
            Layers.Clear();
            LayerCount.Text = "0";
            UpdateChrome();
            return;
        }

        _updatingUi = true;
        Title = Path.GetFileName(_document.PackagePath.TrimEnd('\\')) + " — CompositorWin";
        Layers.Clear();
        foreach (var layer in _document.Manifest.Layers.AsEnumerable().Reverse())
        {
            Layers.Add(new LayerRow(layer, MakeThumb(_document, layer)));
        }

        LayerCount.Text = _document.Manifest.Layers.Count.ToString(CultureInfo.InvariantCulture);
        var active = Layers.FirstOrDefault(l => l.Id.Equals(_document.Manifest.ActiveLayerId, StringComparison.OrdinalIgnoreCase)) ?? Layers.FirstOrDefault();
        LayerList.SelectedItem = active;
        SyncSliders(active);
        RenderCanvas();
        _watcher.Watch(_document.PackagePath);
        _watcher.RememberFingerprint();
        RefreshTabs();
        _updatingUi = false;
        ApplyZoom();
        DrawOverlay();
        UpdateChrome();
    }

    private void RefreshLayerThumbs()
    {
        if (_document is null) return;
        var selected = (LayerList.SelectedItem as LayerRow)?.Id;
        Layers.Clear();
        foreach (var layer in _document.Manifest.Layers.AsEnumerable().Reverse())
        {
            Layers.Add(new LayerRow(layer, MakeThumb(_document, layer)));
        }

        LayerCount.Text = _document.Manifest.Layers.Count.ToString(CultureInfo.InvariantCulture);
        LayerList.SelectedItem = Layers.FirstOrDefault(l => l.Id.Equals(selected ?? _document.Manifest.ActiveLayerId, StringComparison.OrdinalIgnoreCase)) ?? Layers.FirstOrDefault();
    }

    private static ImageSource? MakeThumb(CompDocument doc, LayerRecord layer)
    {
        try
        {
            if (string.IsNullOrEmpty(layer.ImageFile)) return null;
            var path = ProjectValidator.SafeImagePath(doc.PackagePath, layer.ImageFile);
            if (!File.Exists(path)) return null;
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = 64;
            img.UriSource = new Uri(path);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }

    private void SyncSliders(LayerRow? active)
    {
        if (active is null) return;
        OpacitySlider.Value = active.Model.Opacity;
        OpacityPercent.Text = ((int)Math.Round(active.Model.Opacity * 100)).ToString(CultureInfo.InvariantCulture);
        SelectBlend(active.Model.BlendMode);
        PosXBox.Text = active.Model.Transform.X.ToString("0.#", CultureInfo.InvariantCulture);
        PosYBox.Text = active.Model.Transform.Y.ToString("0.#", CultureInfo.InvariantCulture);
        SizeWBox.Text = active.Model.Transform.Width.ToString("0.#", CultureInfo.InvariantCulture);
        SizeHBox.Text = active.Model.Transform.Height.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private void QueueRender()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _renderQueued = false;
            RenderCanvas();
        }, DispatcherPriority.Background);
    }

    private void RenderCanvas()
    {
        if (_document is null) return;
        using var bitmap = DocumentCompositor.Composite(_document, _zoom);
        CanvasImage.Source = ToBitmapSource(bitmap);
        ApplyZoom();
    }

    private BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        using var converted = bitmap.ColorType == SKColorType.Bgra8888 ? null : bitmap.Copy(SKColorType.Bgra8888);
        var src = converted ?? bitmap;
        var info = src.Info;
        if (_canvasWb is null || _canvasWb.PixelWidth != info.Width || _canvasWb.PixelHeight != info.Height)
        {
            _canvasWb = new WriteableBitmap(info.Width, info.Height, 96, 96, PixelFormats.Bgra32, null);
        }

        _canvasWb.Lock();
        try
        {
            unsafe { Buffer.MemoryCopy((void*)src.GetPixels(), (void*)_canvasWb.BackBuffer, info.BytesSize, info.BytesSize); }
            _canvasWb.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
        }
        finally { _canvasWb.Unlock(); }

        return _canvasWb;
    }

    private System.Windows.Point DocPt(MouseEventArgs e)
    {
        var p = e.GetPosition(CanvasImage);
        var src = CanvasImage.Source as BitmapSource;
        var sx = src is null ? 1 : src.PixelWidth / Math.Max(1, CanvasImage.ActualWidth);
        var sy = src is null ? 1 : src.PixelHeight / Math.Max(1, CanvasImage.ActualHeight);
        return new System.Windows.Point(p.X * sx, p.Y * sy);
    }

    private void Canvas_Down(object sender, MouseButtonEventArgs e)
    {
        if (_document is null) return;
        _down = true;
        _start = _last = DocPt(e);
        _stroke.Clear();
        _stroke.Add(new SKPoint((float)_start.X, (float)_start.Y));
        CanvasHost.CaptureMouse();
        _scrollX = CanvasScroll.HorizontalOffset;
        _scrollY = CanvasScroll.VerticalOffset;
        _panStart = e.GetPosition(CanvasScroll);
        if (_tool is EditorTool.Brush or EditorTool.Eraser or EditorTool.Mask or EditorTool.Clone or EditorTool.Heal or EditorTool.Blur)
        {
            _workPixels?.Dispose();
            if (_tool == EditorTool.Mask)
            {
                var layer = Active();
                _workPixels = layer is not null && !string.IsNullOrEmpty(layer.MaskFile)
                    ? LayerRasterCache.Clone(ProjectValidator.SafeImagePath(_document.PackagePath, layer.MaskFile))
                    : new SKBitmap(_document.Manifest.Width, _document.Manifest.Height, SKColorType.Gray8, SKAlphaType.Opaque);
                if (string.IsNullOrEmpty(layer?.MaskFile))
                {
                    using var c = new SKCanvas(_workPixels);
                    c.Clear(SKColors.White);
                }
            }
            else if (Active() is { } layer)
            {
                _workPixels = ProjectStore.LoadLayerPixels(_document, layer);
            }
        }
        if (_tool == EditorTool.Clone && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            _cloneSource = new SKPoint((float)_start.X, (float)_start.Y);
            Status("已采样仿制源");
        }

        if (_tool == EditorTool.Eyedropper)
        {
            SampleColor((int)_start.X, (int)_start.Y);
            _down = false;
            CanvasHost.ReleaseMouseCapture();
            return;
        }

        if (_tool == EditorTool.Zoom)
        {
            _zoom = Math.Clamp(_zoom * (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 0.8 : 1.25), 0.05, 8);
            ApplyZoom();
            _down = false;
            CanvasHost.ReleaseMouseCapture();
            return;
        }

        if (_tool == EditorTool.Wand)
        {
            using var px = ProjectStore.LoadLayerPixels(_document, Active() ?? _document.Manifest.Layers[0]);
            _selection?.Dispose();
            _selection = PixelTools.MagicWand(px, (int)_start.X, (int)_start.Y, 32);
            DrawOverlay();
            _down = false;
            CanvasHost.ReleaseMouseCapture();
        }

        if (_tool == EditorTool.Type)
        {
            var text = Prompt("输入文字");
            if (!string.IsNullOrWhiteSpace(text))
            {
                var size = double.TryParse(FontSizeBox.Text, out var fs) ? fs : 48;
                var font = FontCombo.SelectedItem as string ?? "Segoe UI";
                ProjectStore.AddTextLayer(_document, new TextRecord { Content = text, FontSize = size, FontName = font, Red = _fg.Red / 255.0, Green = _fg.Green / 255.0, Blue = _fg.Blue / 255.0 }, _start.X, _start.Y, 400, 120);
                BindDocument();
            }

            _down = false;
            CanvasHost.ReleaseMouseCapture();
        }
    }

    private void Canvas_Move(object sender, MouseEventArgs e)
    {
        if (!_down || _document is null) return;
        var p = DocPt(e);
        if (_tool == EditorTool.Hand)
        {
            var now = e.GetPosition(CanvasScroll);
            CanvasScroll.ScrollToHorizontalOffset(Math.Max(0, _scrollX - (now.X - _panStart.X)));
            CanvasScroll.ScrollToVerticalOffset(Math.Max(0, _scrollY - (now.Y - _panStart.Y)));
            return;
        }

        if (_tool is EditorTool.Brush or EditorTool.Eraser or EditorTool.Mask or EditorTool.Clone or EditorTool.Heal or EditorTool.Blur or EditorTool.Lasso)
        {
            _stroke.Add(new SKPoint((float)p.X, (float)p.Y));
            if (_tool is not EditorTool.Lasso) PaintStroke(finished: false);
        }

        _last = p;
        DrawOverlay();
    }

    private void Canvas_Up(object sender, MouseButtonEventArgs e)
    {
        if (!_down || _document is null) { _down = false; return; }
        _down = false;
        CanvasHost.ReleaseMouseCapture();
        var p = DocPt(e);
        if (_tool is EditorTool.Brush or EditorTool.Eraser or EditorTool.Mask or EditorTool.Clone or EditorTool.Heal or EditorTool.Blur)
        {
            PaintStroke(finished: true);
        }
        else if (_tool == EditorTool.Marquee)
        {
            var rect = SKRect.Create((float)Math.Min(_start.X, p.X), (float)Math.Min(_start.Y, p.Y), (float)Math.Abs(p.X - _start.X), (float)Math.Abs(p.Y - _start.Y));
            _selection?.Dispose();
            _selection = PixelTools.Marquee(_document.Manifest.Width, _document.Manifest.Height, rect, ellipse: false);
        }
        else if (_tool == EditorTool.Lasso)
        {
            _selection?.Dispose();
            _selection = PixelTools.Lasso(_document.Manifest.Width, _document.Manifest.Height, _stroke);
        }
        else if (_tool == EditorTool.Gradient && Active() is { } layer)
        {
            using var px = ProjectStore.LoadLayerPixels(_document, layer);
            PixelTools.Gradient(px, new SKPoint((float)_start.X, (float)_start.Y), new SKPoint((float)p.X, (float)p.Y), _fg, _bg, _selection);
            ProjectStore.SaveLayerPixels(_document, layer, px);
            BindDocument();
        }
        else if (_tool == EditorTool.Shape)
        {
            var w = Math.Max(8, Math.Abs(p.X - _start.X));
            var h = Math.Max(8, Math.Abs(p.Y - _start.Y));
            ProjectStore.AddShapeLayer(_document, new ShapeRecord { Kind = "rectangle", Red = 0.15, Green = 0.45, Blue = 0.9 }, Math.Min(_start.X, p.X), Math.Min(_start.Y, p.Y), w, h);
            BindDocument();
        }
        else if (_tool == EditorTool.Crop)
        {
            CanvasOps.Crop(_document, (int)Math.Min(_start.X, p.X), (int)Math.Min(_start.Y, p.Y), (int)Math.Abs(p.X - _start.X), (int)Math.Abs(p.Y - _start.Y));
            BindDocument();
        }
        else if (_tool == EditorTool.Move && Active() is { } mv)
        {
            mv.Transform.Origin = [mv.Transform.X + (p.X - _start.X), mv.Transform.Y + (p.Y - _start.Y)];
            Snap(mv);
            ProjectStore.Save(_document);
            BindDocument();
        }

        DrawOverlay();
    }

    private void Snap(LayerRecord layer)
    {
        if (_document?.Manifest.Guides is null) return;
        foreach (var g in _document.Manifest.Guides)
        {
            if (g.Axis == "vertical" && Math.Abs(layer.Transform.X - g.Position) < 8)
            {
                layer.Transform.Origin = [g.Position, layer.Transform.Y];
            }

            if (g.Axis == "horizontal" && Math.Abs(layer.Transform.Y - g.Position) < 8)
            {
                layer.Transform.Origin = [layer.Transform.X, g.Position];
            }
        }
    }

    private void PaintStroke(bool finished)
    {
        if (_document is null || Active() is null || _stroke.Count == 0) return;
        var layer = Active()!;
        var radius = (float)BrushSlider.Value;
        var hardness = (float)HardSlider.Value;
        var opacity = (float)PaintOpacity.Value;
        var color = _fg.WithAlpha((byte)Math.Clamp(opacity * 255, 8, 255));
        _workPixels ??= _tool == EditorTool.Mask
            ? new SKBitmap(_document.Manifest.Width, _document.Manifest.Height, SKColorType.Gray8, SKAlphaType.Opaque)
            : ProjectStore.LoadLayerPixels(_document, layer);

        if (_tool == EditorTool.Clone && _cloneSource is { } src)
        {
            var last = _stroke[^1];
            var delta = new SKPoint(last.X - (float)_start.X, last.Y - (float)_start.Y);
            PixelTools.CloneStamp(_workPixels, last, new SKPoint(src.X + delta.X, src.Y + delta.Y), radius, _selection);
        }
        else if (_tool == EditorTool.Heal)
        {
            var last = _stroke[^1];
            PixelTools.CloneStamp(_workPixels, last, new SKPoint(last.X - radius, last.Y - radius), radius, _selection);
        }
        else if (_tool == EditorTool.Blur)
        {
            PixelTools.Smear(_workPixels, _stroke.Count <= 2 ? _stroke : _stroke.TakeLast(2).ToList(), radius);
        }
        else if (_tool == EditorTool.Mask)
        {
            var pts = _stroke.Count <= 2 ? _stroke : _stroke.TakeLast(2).ToList();
            PixelTools.PaintBrush(_workPixels, pts, radius, SKColors.White, 0.7f, erase: false);
        }
        else
        {
            var pts = _stroke.Count <= 2 ? _stroke : _stroke.TakeLast(2).ToList();
            PixelTools.PaintBrush(_workPixels, pts, radius, color, hardness, _tool == EditorTool.Eraser, _selection);
        }

        if (!finished)
        {
            return;
        }

        if (_tool == EditorTool.Mask)
        {
            ProjectStore.SetLayerMask(_document, layer.Id, _workPixels);
        }
        else
        {
            ProjectStore.SaveLayerPixels(_document, layer, _workPixels);
        }

        _workPixels.Dispose();
        _workPixels = null;
        RenderCanvas();
        RefreshLayerThumbs();
    }

    private void DrawOverlay()
    {
        Overlay.Children.Clear();
        Overlay.Width = CanvasHost.Width;
        Overlay.Height = CanvasHost.Height;
        if (_document?.Manifest.Guides is not null)
        {
            foreach (var g in _document.Manifest.Guides)
            {
                var line = new Line { Stroke = Brushes.Cyan, StrokeThickness = 1 };
                if (g.Axis == "vertical")
                {
                    line.X1 = line.X2 = g.Position * _zoom;
                    line.Y1 = 0;
                    line.Y2 = Overlay.Height;
                }
                else
                {
                    line.Y1 = line.Y2 = g.Position * _zoom;
                    line.X1 = 0;
                    line.X2 = Overlay.Width;
                }

                Overlay.Children.Add(line);
            }
        }

        if (_down && _tool is EditorTool.Brush or EditorTool.Eraser or EditorTool.Clone or EditorTool.Heal or EditorTool.Blur or EditorTool.Mask && _stroke.Count > 1)
        {
            var poly = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(180, _fg.Red, _fg.Green, _fg.Blue)),
                StrokeThickness = Math.Max(1, BrushSlider.Value * _zoom),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            foreach (var pt in _stroke)
            {
                poly.Points.Add(new System.Windows.Point(pt.X * _zoom, pt.Y * _zoom));
            }

            Overlay.Children.Add(poly);
        }

        if (_down && _tool == EditorTool.Lasso && _stroke.Count > 1)
        {
            var poly = new Polyline
            {
                Stroke = Brushes.White,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                StrokeThickness = 1
            };
            foreach (var pt in _stroke)
            {
                poly.Points.Add(new System.Windows.Point(pt.X * _zoom, pt.Y * _zoom));
            }

            Overlay.Children.Add(poly);
        }

        if (_down && _tool is EditorTool.Marquee or EditorTool.Crop or EditorTool.Shape)
        {
            Overlay.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Stroke = Brushes.White,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Width = Math.Abs(_last.X - _start.X) * _zoom,
                Height = Math.Abs(_last.Y - _start.Y) * _zoom,
                Margin = new Thickness(Math.Min(_start.X, _last.X) * _zoom, Math.Min(_start.Y, _last.Y) * _zoom, 0, 0)
            });
        }
    }

    private void MutatePixels(Action<SKBitmap> edit)
    {
        if (_document is null || Active() is null) return;
        using var px = ProjectStore.LoadLayerPixels(_document, Active()!);
        edit(px);
        ProjectStore.SaveLayerPixels(_document, Active()!, px);
        BindDocument();
    }

    private LayerRecord? Active()
    {
        if (_document is null) return null;
        return LayerList.SelectedItem is LayerRow row ? row.Model : _document.Manifest.Layers.LastOrDefault();
    }

    private void LayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _document is null || LayerList.SelectedItem is not LayerRow row) return;
        _document.Manifest.ActiveLayerId = row.Id;
        _updatingUi = true;
        SyncSliders(row);
        _updatingUi = false;
    }

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingUi || Active() is null) return;
        Active()!.Opacity = OpacitySlider.Value;
        OpacityPercent.Text = ((int)Math.Round(OpacitySlider.Value * 100)).ToString(CultureInfo.InvariantCulture);
        _dirtyFromUi = true;
        _opacityTimer.Stop();
        _opacityTimer.Start();
    }

    private void Blend_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || Active() is null) return;
        if (BlendCombo.SelectedItem is ComboBoxItem item && item.Tag is string mode)
        {
            Active()!.BlendMode = mode;
            _dirtyFromUi = true;
            QueueRender();
        }
    }

    private void Visibility_Click(object sender, RoutedEventArgs e)
    {
        _dirtyFromUi = true;
        QueueRender();
    }

    private void Canvas_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 0.9), 0.05, 8);
        ApplyZoom();
        e.Handled = true;
    }

    private void ApplyZoom()
    {
        if (CanvasImage.Source is not BitmapSource bmp)
        {
            ZoomStatus.Text = "—";
            ZoomBox.Text = "100";
            return;
        }

        CanvasHost.Width = bmp.PixelWidth * _zoom;
        CanvasHost.Height = bmp.PixelHeight * _zoom;
        CanvasImage.Width = CanvasHost.Width;
        CanvasImage.Height = CanvasHost.Height;
        RenderOptions.SetBitmapScalingMode(CanvasImage, Math.Abs(_zoom - 1) < 0.02 ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        ZoomStatus.Text = $"{_zoom * 100:0.#}%";
        ZoomBox.Text = $"{_zoom * 100:0.#}";
        DrawOverlay();
        DrawRulers();
    }

    private void DrawRulers()
    {
        RulerH.Children.Clear();
        RulerV.Children.Clear();
        if (!_showsRulers || _document is null) return;
        RulerH.Width = Math.Max(CanvasHost.Width, 200);
        RulerV.Height = Math.Max(CanvasHost.Height, 200);
        var step = _zoom >= 2 ? 10 : _zoom >= 1 ? 50 : 100;
        for (var x = 0; x < _document.Manifest.Width; x += step)
        {
            var px = x * _zoom;
            RulerH.Children.Add(new Line { X1 = px, X2 = px, Y1 = 10, Y2 = 16, Stroke = Brushes.Gray, StrokeThickness = 1 });
            if (x % (step * 2) == 0)
            {
                var label = new TextBlock { Text = x.ToString(CultureInfo.InvariantCulture), Foreground = Brushes.Gray, FontSize = 8 };
                Canvas.SetLeft(label, px + 2);
                Canvas.SetTop(label, 0);
                RulerH.Children.Add(label);
            }
        }

        for (var y = 0; y < _document.Manifest.Height; y += step)
        {
            var py = y * _zoom;
            RulerV.Children.Add(new Line { Y1 = py, Y2 = py, X1 = 10, X2 = 16, Stroke = Brushes.Gray, StrokeThickness = 1 });
        }
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        var vw = Math.Max(80, CanvasScroll.ActualWidth - 96);
        var vh = Math.Max(80, CanvasScroll.ActualHeight - 96);
        _zoom = Math.Clamp(Math.Min(vw / _document.Manifest.Width, vh / _document.Manifest.Height), 0.05, 8);
        ApplyZoom();
    }

    private void Actual_Click(object sender, RoutedEventArgs e)
    {
        _zoom = 1;
        ApplyZoom();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * 1.25, 0.05, 8);
        ApplyZoom();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Clamp(_zoom / 1.25, 0.05, 8);
        ApplyZoom();
    }

    private void ZoomBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(ZoomBox.Text.Replace("%", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            _zoom = Math.Clamp(z / 100.0, 0.05, 8);
            ApplyZoom();
        }
    }

    private void TransformBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Active() is null) return;
        if (!double.TryParse(PosXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) x = Active()!.Transform.X;
        if (!double.TryParse(PosYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) y = Active()!.Transform.Y;
        if (!double.TryParse(SizeWBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) w = Active()!.Transform.Width;
        if (!double.TryParse(SizeHBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) h = Active()!.Transform.Height;
        if (LockRatio.IsChecked == true && Active()!.Transform.Width > 0)
        {
            var ratio = Active()!.Transform.Height / Active()!.Transform.Width;
            if (sender == SizeWBox) h = w * ratio;
            if (sender == SizeHBox) w = h / Math.Max(0.001, ratio);
        }

        Active()!.Transform.Origin = [x, y];
        Active()!.Transform.Size = [Math.Max(1, w), Math.Max(1, h)];
        _dirtyFromUi = true;
        ProjectStore.Save(_document!);
        BindDocument();
    }

    private void OpacityPercent_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || Active() is null) return;
        if (!double.TryParse(OpacityPercent.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return;
        Active()!.Opacity = Math.Clamp(p / 100.0, 0, 1);
        OpacitySlider.Value = Active()!.Opacity;
        _dirtyFromUi = true;
        RenderCanvas();
    }

    private void SwapColors_Click(object sender, MouseButtonEventArgs e)
    {
        (_fg, _bg) = (_bg, _fg);
        FgSwatch.Background = new SolidColorBrush(Color.FromRgb(_fg.Red, _fg.Green, _fg.Blue));
    }

    private void SampleColor(int x, int y)
    {
        if (_document is null) return;
        using var bmp = DocumentCompositor.Composite(_document);
        x = Math.Clamp(x, 0, bmp.Width - 1);
        y = Math.Clamp(y, 0, bmp.Height - 1);
        _fg = bmp.GetPixel(x, y);
        FgSwatch.Background = new SolidColorBrush(Color.FromRgb(_fg.Red, _fg.Green, _fg.Blue));
        Status($"前景 #{_fg.Red:X2}{_fg.Green:X2}{_fg.Blue:X2}");
    }

    private void BlankLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        ProjectStore.AddBlankLayer(_document);
        BindDocument();
    }

    private void DeleteLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || Active() is null) return;
        ProjectStore.DeleteLayer(_document, Active()!.Id);
        BindDocument();
    }

    private void Deselect_Click(object sender, RoutedEventArgs e)
    {
        _selection?.Dispose();
        _selection = null;
        DrawOverlay();
        Status("已取消选择");
    }

    private void Rulers_Click(object sender, RoutedEventArgs e)
    {
        _showsRulers = !_showsRulers;
        UpdateChrome();
        DrawRulers();
    }

    private void AddMask_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || Active() is null) return;
        using var mask = new SKBitmap(_document.Manifest.Width, _document.Manifest.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var c = new SKCanvas(mask);
        c.Clear(SKColors.White);
        ProjectStore.SetLayerMask(_document, Active()!.Id, mask);
        BindDocument();
    }

    private void Splitter_Down(object sender, MouseButtonEventArgs e)
    {
        _splitterDown = true;
        _splitterStart = e.GetPosition(this).X;
        _layersStart = LayersCol.ActualWidth;
        ((UIElement)sender).CaptureMouse();
    }

    private void Splitter_Move(object sender, MouseEventArgs e)
    {
        if (!_splitterDown) return;
        var dx = e.GetPosition(this).X - _splitterStart;
        LayersCol.Width = new GridLength(Math.Clamp(_layersStart - dx, 202, 352));
    }

    private void Splitter_Up(object sender, MouseButtonEventArgs e)
    {
        _splitterDown = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var file in files)
        {
            if (Directory.Exists(file) && File.Exists(Path.Combine(file, "manifest.json")))
            {
                LoadPackage(file, false);
            }
            else if (File.Exists(file))
            {
                EnsureDocFromImage(file);
                ProjectStore.AddImageLayer(_document!, file);
                BindDocument();
            }
        }
    }

    private string? Prompt(string title)
    {
        var box = new TextBox { Text = "文字", Margin = new Thickness(0, 0, 0, 12) };
        var ok = new Button { Content = "确定", Style = (Style)FindResource("AccentButton"), Width = 88, Height = 32, HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(ok, Dock.Bottom);
        panel.Children.Add(ok);
        panel.Children.Add(box);
        var w = new Window
        {
            Title = title,
            Width = 420,
            Height = 160,
            Owner = this,
            Content = panel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("Bg1"),
            ResizeMode = ResizeMode.NoResize
        };
        ok.Click += (_, _) => w.DialogResult = true;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                w.DialogResult = true;
            }
        };
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true ? box.Text : null;
    }

    private void Status(string text) => StatusText.Text = text;

    private void SelectBlend(string mode)
    {
        foreach (ComboBoxItem item in BlendCombo.Items)
        {
            if (string.Equals(item.Tag as string, mode, StringComparison.OrdinalIgnoreCase))
            {
                BlendCombo.SelectedItem = item;
                return;
            }
        }
    }

    private static string BlendLabel(string mode) => mode switch
    {
        "Normal" => "正常",
        "Darken" => "变暗",
        "Multiply" => "正片叠底",
        "Color Burn" => "颜色加深",
        "Linear Burn" => "线性加深",
        "Lighten" => "变亮",
        "Screen" => "滤色",
        "Color Dodge" => "颜色减淡",
        "Linear Dodge (Add)" => "线性减淡（添加）",
        "Overlay" => "叠加",
        "Soft Light" => "柔光",
        "Hard Light" => "强光",
        "Vivid Light" => "亮光",
        "Linear Light" => "线性光",
        "Pin Light" => "点光",
        "Hard Mix" => "实色混合",
        "Difference" => "差值",
        "Exclusion" => "排除",
        "Subtract" => "减去",
        "Divide" => "划分",
        "Hue" => "色相",
        "Saturation" => "饱和度",
        "Color" => "颜色",
        "Luminosity" => "明度",
        _ => mode
    };
    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static Int32Rect Bounds(SKBitmap mask)
    {
        var minX = mask.Width;
        var minY = mask.Height;
        var maxX = 0;
        var maxY = 0;
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                if (mask.GetPixel(x, y).Red < 16) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return new Int32Rect(minX, minY, Math.Max(1, maxX - minX + 1), Math.Max(1, maxY - minY + 1));
    }
}

public sealed class LayerRow : INotifyPropertyChanged
{
    public LayerRecord Model { get; }
    public ImageSource? Thumb { get; }
    public LayerRow(LayerRecord model, ImageSource? thumb)
    {
        Model = model;
        Thumb = thumb;
    }
    public string Id => Model.Id;
    public string DisplayName
    {
        get
        {
            var extra = Model.Adjustment is not null ? "  [调整]" : Model.IsGroup ? "  [组]" : Model.Text is not null ? "  [字]" : "";
            if (!string.IsNullOrEmpty(Model.MaskFile)) extra += "  [蒙版]";
            if (Model.MaskSourceId is not null) extra += "  [剪贴]";
            return (string.IsNullOrEmpty(Model.ParentId) ? "" : "    ") + Model.Name + extra;
        }
    }
    public bool IsVisible { get => Model.IsVisible; set { Model.IsVisible = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
