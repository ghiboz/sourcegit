using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using AvaloniaEdit.Utils;

namespace SourceGit.Views
{
    public class TextDiffViewChunk
    {
        public double Y { get; set; } = 0.0;
        public double Height { get; set; } = 0.0;
        public int StartIdx { get; set; } = 0;
        public int EndIdx { get; set; } = 0;
        public bool Combined { get; set; } = true;
        public bool IsOldSide { get; set; } = false;

        public bool ShouldReplace(TextDiffViewChunk old)
        {
            if (old == null)
                return true;

            return Math.Abs(Y - old.Y) > 0.001 ||
                Math.Abs(Height - old.Height) > 0.001 ||
                StartIdx != old.StartIdx ||
                EndIdx != old.EndIdx ||
                Combined != Combined ||
                IsOldSide != IsOldSide;
        }
    }

    public class ThemedTextDiffPresenter : TextEditor
    {
        public class VerticalSeperatorMargin : AbstractMargin
        {
            public override void Render(DrawingContext context)
            {
                var presenter = this.FindAncestorOfType<ThemedTextDiffPresenter>();
                if (presenter != null)
                {
                    var pen = new Pen(presenter.LineBrush);
                    context.DrawLine(pen, new Point(0, 0), new Point(0, Bounds.Height));
                }
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                return new Size(1, 0);
            }
        }

        public class LineNumberMargin : AbstractMargin
        {
            public LineNumberMargin(bool usePresenter, bool isOld)
            {
                _usePresenter = usePresenter;
                _isOld = isOld;
                ClipToBounds = true;
            }

            public override void Render(DrawingContext context)
            {
                var presenter = this.FindAncestorOfType<ThemedTextDiffPresenter>();
                if (presenter == null)
                    return;

                var isOld = _isOld;
                if (_usePresenter)
                    isOld = presenter.IsOld;

                var lines = presenter.GetLines();
                var view = TextView;
                if (view != null && view.VisualLinesValid)
                {
                    var typeface = view.CreateTypeface();
                    foreach (var line in view.VisualLines)
                    {
                        var index = line.FirstDocumentLine.LineNumber;
                        if (index > lines.Count)
                            break;

                        var info = lines[index - 1];
                        var lineNumber = isOld ? info.OldLine : info.NewLine;
                        if (string.IsNullOrEmpty(lineNumber))
                            continue;

                        var y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset;
                        var txt = new FormattedText(
                            lineNumber,
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            presenter.FontSize,
                            presenter.Foreground);
                        context.DrawText(txt, new Point(Bounds.Width - txt.Width, y));
                    }
                }
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                var presenter = this.FindAncestorOfType<ThemedTextDiffPresenter>();
                if (presenter == null)
                    return new Size(32, 0);

                var maxLineNumber = presenter.GetMaxLineNumber();
                var typeface = TextView.CreateTypeface();
                var test = new FormattedText(
                    $"{maxLineNumber}",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    presenter.FontSize,
                    Brushes.White);
                return new Size(test.Width, 0);
            }

            protected override void OnDataContextChanged(EventArgs e)
            {
                base.OnDataContextChanged(e);
                InvalidateMeasure();
            }

            private bool _usePresenter = false;
            private bool _isOld = false;
        }

        public class LineBackgroundRenderer : IBackgroundRenderer
        {
            public KnownLayer Layer => KnownLayer.Background;

            public LineBackgroundRenderer(ThemedTextDiffPresenter presenter)
            {
                _presenter = presenter;
            }

            public void Draw(TextView textView, DrawingContext drawingContext)
            {
                if (_presenter.Document == null || !textView.VisualLinesValid)
                    return;

                var lines = _presenter.GetLines();
                var width = textView.Bounds.Width;
                foreach (var line in textView.VisualLines)
                {
                    if (line.FirstDocumentLine == null)
                        continue;

                    var index = line.FirstDocumentLine.LineNumber;
                    if (index > lines.Count)
                        break;

                    var info = lines[index - 1];
                    var bg = GetBrushByLineType(info.Type);
                    if (bg == null)
                        continue;

                    var y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop) - textView.VerticalOffset;
                    drawingContext.DrawRectangle(bg, null, new Rect(0, y, width, line.Height));
                }
            }

            private IBrush GetBrushByLineType(Models.TextDiffLineType type)
            {
                switch (type)
                {
                    case Models.TextDiffLineType.None:
                        return _presenter.EmptyContentBackground;
                    case Models.TextDiffLineType.Added:
                        return _presenter.AddedContentBackground;
                    case Models.TextDiffLineType.Deleted:
                        return _presenter.DeletedContentBackground;
                    default:
                        return null;
                }
            }

            private ThemedTextDiffPresenter _presenter = null;
        }

        public class LineStyleTransformer : DocumentColorizingTransformer
        {
            public LineStyleTransformer(ThemedTextDiffPresenter presenter)
            {
                _presenter = presenter;
            }

            protected override void ColorizeLine(DocumentLine line)
            {
                var lines = _presenter.GetLines();
                var idx = line.LineNumber;
                if (idx > lines.Count)
                    return;

                var info = lines[idx - 1];
                if (info.Type == Models.TextDiffLineType.Indicator)
                {
                    ChangeLinePart(line.Offset, line.EndOffset, v =>
                    {
                        v.TextRunProperties.SetForegroundBrush(_presenter.IndicatorForeground);
                        v.TextRunProperties.SetTypeface(new Typeface(_presenter.FontFamily, FontStyle.Italic));
                    });

                    return;
                }

                if (info.Highlights.Count > 0)
                {
                    var bg = info.Type == Models.TextDiffLineType.Added ? _presenter.AddedHighlightBrush : _presenter.DeletedHighlightBrush;
                    foreach (var highlight in info.Highlights)
                    {
                        ChangeLinePart(line.Offset + highlight.Start, line.Offset + highlight.Start + highlight.Count, v =>
                        {
                            v.TextRunProperties.SetBackgroundBrush(bg);
                        });
                    }
                }
            }

            private readonly ThemedTextDiffPresenter _presenter;
        }

        public static readonly StyledProperty<string> FileNameProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, string>(nameof(FileName), string.Empty);

        public string FileName
        {
            get => GetValue(FileNameProperty);
            set => SetValue(FileNameProperty, value);
        }

        public static readonly StyledProperty<bool> IsOldProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, bool>(nameof(IsOld));

        public bool IsOld
        {
            get => GetValue(IsOldProperty);
            set => SetValue(IsOldProperty, value);
        }

        public static readonly StyledProperty<IBrush> LineBrushProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(LineBrush), new SolidColorBrush(Colors.DarkGray));

        public IBrush LineBrush
        {
            get => GetValue(LineBrushProperty);
            set => SetValue(LineBrushProperty, value);
        }

        public static readonly StyledProperty<IBrush> EmptyContentBackgroundProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(EmptyContentBackground), new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)));

        public IBrush EmptyContentBackground
        {
            get => GetValue(EmptyContentBackgroundProperty);
            set => SetValue(EmptyContentBackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> AddedContentBackgroundProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(AddedContentBackground), new SolidColorBrush(Color.FromArgb(60, 0, 255, 0)));

        public IBrush AddedContentBackground
        {
            get => GetValue(AddedContentBackgroundProperty);
            set => SetValue(AddedContentBackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> DeletedContentBackgroundProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(DeletedContentBackground), new SolidColorBrush(Color.FromArgb(60, 255, 0, 0)));

        public IBrush DeletedContentBackground
        {
            get => GetValue(DeletedContentBackgroundProperty);
            set => SetValue(DeletedContentBackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> AddedHighlightBrushProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(AddedHighlightBrush), new SolidColorBrush(Color.FromArgb(90, 0, 255, 0)));

        public IBrush AddedHighlightBrush
        {
            get => GetValue(AddedHighlightBrushProperty);
            set => SetValue(AddedHighlightBrushProperty, value);
        }

        public static readonly StyledProperty<IBrush> DeletedHighlightBrushProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(DeletedHighlightBrush), new SolidColorBrush(Color.FromArgb(80, 255, 0, 0)));

        public IBrush DeletedHighlightBrush
        {
            get => GetValue(DeletedHighlightBrushProperty);
            set => SetValue(DeletedHighlightBrushProperty, value);
        }

        public static readonly StyledProperty<IBrush> IndicatorForegroundProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(IndicatorForeground), Brushes.Gray);

        public IBrush IndicatorForeground
        {
            get => GetValue(IndicatorForegroundProperty);
            set => SetValue(IndicatorForegroundProperty, value);
        }

        public static readonly StyledProperty<bool> UseSyntaxHighlightingProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, bool>(nameof(UseSyntaxHighlighting));

        public bool UseSyntaxHighlighting
        {
            get => GetValue(UseSyntaxHighlightingProperty);
            set => SetValue(UseSyntaxHighlightingProperty, value);
        }

        public static readonly StyledProperty<bool> ShowHiddenSymbolsProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, bool>(nameof(ShowHiddenSymbols));

        public bool ShowHiddenSymbols
        {
            get => GetValue(ShowHiddenSymbolsProperty);
            set => SetValue(ShowHiddenSymbolsProperty, value);
        }

        public static readonly StyledProperty<bool> EnableChunkSelectionProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, bool>(nameof(EnableChunkSelection));

        public bool EnableChunkSelection
        {
            get => GetValue(EnableChunkSelectionProperty);
            set => SetValue(EnableChunkSelectionProperty, value);
        }

        public static readonly StyledProperty<TextDiffViewChunk> SelectedChunkProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, TextDiffViewChunk>(nameof(SelectedChunk));

        public TextDiffViewChunk SelectedChunk
        {
            get => GetValue(SelectedChunkProperty);
            set => SetValue(SelectedChunkProperty, value);
        }

        protected override Type StyleKeyOverride => typeof(TextEditor);

        public ThemedTextDiffPresenter(TextArea area, TextDocument doc) : base(area, doc)
        {
            IsReadOnly = true;
            ShowLineNumbers = false;
            BorderThickness = new Thickness(0);

            _lineStyleTransformer = new LineStyleTransformer(this);

            TextArea.TextView.Margin = new Thickness(4, 0);
            TextArea.TextView.Options.EnableHyperlinks = false;
            TextArea.TextView.Options.EnableEmailHyperlinks = false;

            TextArea.TextView.BackgroundRenderers.Add(new LineBackgroundRenderer(this));
            TextArea.TextView.LineTransformers.Add(_lineStyleTransformer);
        }

        public virtual List<Models.TextDiffLine> GetLines()
        {
            return [];
        }

        public virtual int GetMaxLineNumber()
        {
            return 0;
        }

        public virtual void UpdateSelectedChunk(double y)
        {
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var chunk = SelectedChunk;
            if (chunk == null || (!chunk.Combined && chunk.IsOldSide != IsOld))
                return;

            var color = (Color)this.FindResource("SystemAccentColor");
            var brush = new SolidColorBrush(color, 0.1);
            var pen = new Pen(color.ToUInt32());
            var rect = new Rect(0, chunk.Y, Bounds.Width, chunk.Height);

            context.DrawRectangle(brush, null, rect);
            context.DrawLine(pen, rect.TopLeft, rect.TopRight);
            context.DrawLine(pen, rect.BottomLeft, rect.BottomRight);
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            TextArea.TextView.ContextRequested += OnTextViewContextRequested;
            TextArea.TextView.PointerMoved += OnTextViewPointerMoved;
            TextArea.TextView.PointerWheelChanged += OnTextViewPointerWheelChanged;

            UpdateTextMate();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            TextArea.TextView.ContextRequested -= OnTextViewContextRequested;
            TextArea.TextView.PointerMoved -= OnTextViewPointerMoved;
            TextArea.TextView.PointerWheelChanged -= OnTextViewPointerWheelChanged;

            if (_textMate != null)
            {
                _textMate.Dispose();
                _textMate = null;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == UseSyntaxHighlightingProperty)
            {
                UpdateTextMate();
            }
            else if (change.Property == ShowHiddenSymbolsProperty)
            {
                var val = change.NewValue is true;
                Options.ShowTabs = val;
                Options.ShowSpaces = val;
            }
            else if (change.Property == FileNameProperty)
            {
                Models.TextMateHelper.SetGrammarByFileName(_textMate, FileName);
            }
            else if (change.Property.Name == "ActualThemeVariant" && change.NewValue != null)
            {
                Models.TextMateHelper.SetThemeByApp(_textMate);
            }
            else if (change.Property == SelectedChunkProperty)
            {
                InvalidateVisual();
            }
        }

        private void OnTextViewContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var selection = TextArea.Selection;
            if (selection.IsEmpty)
                return;

            var copy = new MenuItem();
            copy.Header = App.Text("Copy");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Click += (_, ev) =>
            {
                App.CopyText(SelectedText);
                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(copy);

            TextArea.TextView.OpenContextMenu(menu);
            e.Handled = true;
        }

        private void OnTextViewPointerMoved(object sender, PointerEventArgs e)
        {
            if (EnableChunkSelection && sender is TextView view)
                UpdateSelectedChunk(e.GetPosition(view).Y + view.VerticalOffset);
        }

        private void OnTextViewPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (EnableChunkSelection && sender is TextView view)
            {
                var y = e.GetPosition(view).Y + view.VerticalOffset;
                Dispatcher.UIThread.Post(() => UpdateSelectedChunk(y));
            }
        }

        protected void TrySetChunk(TextDiffViewChunk chunk)
        {
            var old = SelectedChunk;
            if (chunk == null)
            {
                if (old != null)
                    SetCurrentValue(SelectedChunkProperty, null);

                return;
            }

            if (chunk.ShouldReplace(old))
                SetCurrentValue(SelectedChunkProperty, chunk);
        }

        protected (int, int) FindRangeByIndex(List<Models.TextDiffLine> lines, int lineIdx)
        {
            var startIdx = -1;
            var endIdx = -1;

            var normalLineCount = 0;
            var modifiedLineCount = 0;

            for (int i = lineIdx; i >= 0; i--)
            {
                var line = lines[i];
                if (line.Type == Models.TextDiffLineType.Indicator)
                {
                    startIdx = i;
                    break;
                }

                if (line.Type == Models.TextDiffLineType.Normal)
                {
                    normalLineCount++;
                    if (normalLineCount >= 2)
                    {
                        startIdx = i;
                        break;
                    }
                }
                else
                {
                    normalLineCount = 0;
                    modifiedLineCount++;
                }
            }

            normalLineCount = lines[lineIdx].Type == Models.TextDiffLineType.Normal ? 1 : 0;
            for (int i = lineIdx + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Type == Models.TextDiffLineType.Indicator)
                {
                    endIdx = i;
                    break;
                }

                if (line.Type == Models.TextDiffLineType.Normal)
                {
                    normalLineCount++;
                    if (normalLineCount >= 2)
                    {
                        endIdx = i;
                        break;
                    }
                }
                else
                {
                    normalLineCount = 0;
                    modifiedLineCount++;
                }
            }

            if (endIdx == -1)
                endIdx = lines.Count - 1;

            return modifiedLineCount > 0 ? (startIdx, endIdx) : (-1, -1);
        }

        private void UpdateTextMate()
        {
            if (UseSyntaxHighlighting)
            {
                if (_textMate == null)
                {
                    TextArea.TextView.LineTransformers.Remove(_lineStyleTransformer);
                    _textMate = Models.TextMateHelper.CreateForEditor(this);
                    TextArea.TextView.LineTransformers.Add(_lineStyleTransformer);
                    Models.TextMateHelper.SetGrammarByFileName(_textMate, FileName);
                }
            }
            else
            {
                if (_textMate != null)
                {
                    _textMate.Dispose();
                    _textMate = null;
                    GC.Collect();

                    TextArea.TextView.Redraw();
                }
            }
        }

        private TextMate.Installation _textMate = null;
        protected LineStyleTransformer _lineStyleTransformer = null;
    }

    public class CombinedTextDiffPresenter : ThemedTextDiffPresenter
    {
        public CombinedTextDiffPresenter() : base(new TextArea(), new TextDocument())
        {
            TextArea.LeftMargins.Add(new LineNumberMargin(false, true) { Margin = new Thickness(8, 0) });
            TextArea.LeftMargins.Add(new VerticalSeperatorMargin());
            TextArea.LeftMargins.Add(new LineNumberMargin(false, false) { Margin = new Thickness(8, 0) });
            TextArea.LeftMargins.Add(new VerticalSeperatorMargin());
        }

        public override List<Models.TextDiffLine> GetLines()
        {
            if (DataContext is Models.TextDiff diff)
                return diff.Lines;
            return [];
        }

        public override int GetMaxLineNumber()
        {
            if (DataContext is Models.TextDiff diff)
                return diff.MaxLineNumber;
            return 0;
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            var scroller = (ScrollViewer)e.NameScope.Find("PART_ScrollViewer");
            if (scroller != null)
            {
                scroller.Bind(ScrollViewer.OffsetProperty, new Binding("SyncScrollOffset", BindingMode.TwoWay));
                scroller.GotFocus += (_, _) => TrySetChunk(null);
            }
        }

        public override void UpdateSelectedChunk(double y)
        {
            var diff = DataContext as Models.TextDiff;
            if (diff == null)
                return;

            var view = TextArea.TextView;
            var selection = TextArea.Selection;
            if (!selection.IsEmpty)
            {
                var startIdx = Math.Min(selection.StartPosition.Line - 1, diff.Lines.Count - 1);
                var endIdx = Math.Min(selection.EndPosition.Line - 1, diff.Lines.Count - 1);

                if (startIdx > endIdx)
                    (startIdx, endIdx) = (endIdx, startIdx);

                var hasChanges = false;
                for (var i = startIdx; i <= endIdx; i++)
                {
                    var line = diff.Lines[i];
                    if (line.Type == Models.TextDiffLineType.Added || line.Type == Models.TextDiffLineType.Deleted)
                    {
                        hasChanges = true;
                        break;
                    }
                }

                if (!hasChanges)
                {
                    TrySetChunk(null);
                    return;
                }

                var firstLineIdx = view.VisualLines[0].FirstDocumentLine.LineNumber - 1;
                var lastLineIdx = view.VisualLines[^1].FirstDocumentLine.LineNumber - 1;
                if (endIdx < firstLineIdx)
                {
                    TrySetChunk(null);
                    return;
                }
                else if (startIdx > lastLineIdx)
                {
                    TrySetChunk(null);
                    return;
                }

                var startLine = view.GetVisualLine(startIdx + 1);
                var endLine = view.GetVisualLine(endIdx + 1);

                var rectStartY = startLine != null ?
                    startLine.GetTextLineVisualYPosition(startLine.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset :
                    0;
                var rectEndY = endLine != null ?
                    endLine.GetTextLineVisualYPosition(endLine.TextLines[^1], VisualYPosition.TextBottom) - view.VerticalOffset :
                    view.Bounds.Height;

                TrySetChunk(new TextDiffViewChunk()
                {
                    Y = rectStartY,
                    Height = rectEndY - rectStartY,
                    StartIdx = startIdx,
                    EndIdx = endIdx,
                    Combined = true,
                    IsOldSide = false,
                });
            }
            else
            {
                var lineIdx = -1;
                foreach (var line in view.VisualLines)
                {
                    var index = line.FirstDocumentLine.LineNumber;
                    if (index > diff.Lines.Count)
                        break;

                    var endY = line.GetTextLineVisualYPosition(line.TextLines[^1], VisualYPosition.TextBottom);
                    if (endY > y)
                    {
                        lineIdx = index - 1;
                        break;
                    }
                }

                if (lineIdx == -1)
                {
                    TrySetChunk(null);
                    return;
                }

                var (startIdx, endIdx) = FindRangeByIndex(diff.Lines, lineIdx);
                if (startIdx == -1)
                {
                    TrySetChunk(null);
                    return;
                }

                var startLine = view.GetVisualLine(startIdx + 1);
                var endLine = view.GetVisualLine(endIdx + 1);

                var rectStartY = startLine != null ?
                    startLine.GetTextLineVisualYPosition(startLine.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset :
                    0;
                var rectEndY = endLine != null ?
                    endLine.GetTextLineVisualYPosition(endLine.TextLines[^1], VisualYPosition.TextBottom) - view.VerticalOffset :
                    view.Bounds.Height;

                TrySetChunk(new TextDiffViewChunk()
                {
                    Y = rectStartY,
                    Height = rectEndY - rectStartY,
                    StartIdx = startIdx,
                    EndIdx = endIdx,
                    Combined = true,
                    IsOldSide = false,
                });
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            var textDiff = DataContext as Models.TextDiff;
            if (textDiff != null)
            {
                var builder = new StringBuilder();
                foreach (var line in textDiff.Lines)
                {
                    if (line.Content.Length > 10000)
                    {
                        builder.Append(line.Content.Substring(0, 1000));
                        builder.Append($"...({line.Content.Length - 1000} character trimmed)");
                        builder.AppendLine();
                    }
                    else
                    {
                        builder.AppendLine(line.Content);
                    }
                }

                Text = builder.ToString();
            }
            else
            {
                Text = string.Empty;
            }

            GC.Collect();
        }
    }

    public class SingleSideTextDiffPresenter : ThemedTextDiffPresenter
    {
        public SingleSideTextDiffPresenter() : base(new TextArea(), new TextDocument())
        {
            TextArea.LeftMargins.Add(new LineNumberMargin(true, false) { Margin = new Thickness(8, 0) });
            TextArea.LeftMargins.Add(new VerticalSeperatorMargin());
        }

        public override List<Models.TextDiffLine> GetLines()
        {
            if (DataContext is ViewModels.TwoSideTextDiff diff)
                return IsOld ? diff.Old : diff.New;
            return [];
        }

        public override int GetMaxLineNumber()
        {
            if (DataContext is ViewModels.TwoSideTextDiff diff)
                return diff.MaxLineNumber;
            return 0;
        }

        public override void UpdateSelectedChunk(double y)
        {
            var diff = DataContext as ViewModels.TwoSideTextDiff;
            if (diff == null)
                return;

            var parent = this.FindAncestorOfType<TextDiffView>();
            if (parent == null)
                return;

            var view = TextArea.TextView;
            var lines = IsOld ? diff.Old : diff.New;
            var selection = TextArea.Selection;
            if (!selection.IsEmpty)
            {
                var startIdx = Math.Min(selection.StartPosition.Line - 1, lines.Count - 1);
                var endIdx = Math.Min(selection.EndPosition.Line - 1, lines.Count - 1);

                if (startIdx > endIdx)
                    (startIdx, endIdx) = (endIdx, startIdx);

                var hasChanges = false;
                for (var i = startIdx; i <= endIdx; i++)
                {
                    var line = lines[i];
                    if (line.Type == Models.TextDiffLineType.Added || line.Type == Models.TextDiffLineType.Deleted)
                    {
                        hasChanges = true;
                        break;
                    }
                }

                if (!hasChanges)
                {
                    TrySetChunk(null);
                    return;
                }

                var firstLineIdx = view.VisualLines[0].FirstDocumentLine.LineNumber - 1;
                var lastLineIdx = view.VisualLines[^1].FirstDocumentLine.LineNumber - 1;
                if (endIdx < firstLineIdx)
                {
                    TrySetChunk(null);
                    return;
                }
                else if (startIdx > lastLineIdx)
                {
                    TrySetChunk(null);
                    return;
                }

                var startLine = view.GetVisualLine(startIdx + 1);
                var endLine = view.GetVisualLine(endIdx + 1);

                var rectStartY = startLine != null ?
                    startLine.GetTextLineVisualYPosition(startLine.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset :
                    0;
                var rectEndY = endLine != null ?
                    endLine.GetTextLineVisualYPosition(endLine.TextLines[^1], VisualYPosition.TextBottom) - view.VerticalOffset :
                    view.Bounds.Height;

                diff.ConvertsToCombinedRange(parent.DataContext as Models.TextDiff, ref startIdx, ref endIdx, IsOld);

                TrySetChunk(new TextDiffViewChunk()
                {
                    Y = rectStartY,
                    Height = rectEndY - rectStartY,
                    StartIdx = startIdx,
                    EndIdx = endIdx,
                    Combined = false,
                    IsOldSide = IsOld,
                });

                return;
            }

            var textDiff = this.FindAncestorOfType<TextDiffView>()?.DataContext as Models.TextDiff;
            if (textDiff != null)
            {
                var lineIdx = -1;
                foreach (var line in view.VisualLines)
                {
                    var index = line.FirstDocumentLine.LineNumber;
                    if (index > lines.Count)
                        break;

                    var endY = line.GetTextLineVisualYPosition(line.TextLines[^1], VisualYPosition.TextBottom);
                    if (endY > y)
                    {
                        lineIdx = index - 1;
                        break;
                    }
                }

                if (lineIdx == -1)
                {
                    TrySetChunk(null);
                    return;
                }

                var (startIdx, endIdx) = FindRangeByIndex(lines, lineIdx);
                if (startIdx == -1)
                {
                    TrySetChunk(null);
                    return;
                }

                var startLine = view.GetVisualLine(startIdx + 1);
                var endLine = view.GetVisualLine(endIdx + 1);

                var rectStartY = startLine != null ?
                    startLine.GetTextLineVisualYPosition(startLine.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset :
                    0;
                var rectEndY = endLine != null ?
                    endLine.GetTextLineVisualYPosition(endLine.TextLines[^1], VisualYPosition.TextBottom) - view.VerticalOffset :
                    view.Bounds.Height;

                TrySetChunk(new TextDiffViewChunk()
                {
                    Y = rectStartY,
                    Height = rectEndY - rectStartY,
                    StartIdx = textDiff.Lines.IndexOf(lines[startIdx]),
                    EndIdx = endIdx == lines.Count - 1 ? textDiff.Lines.Count - 1 : textDiff.Lines.IndexOf(lines[endIdx]),
                    Combined = true,
                    IsOldSide = false,
                });
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            _scrollViewer = this.FindDescendantOfType<ScrollViewer>();
            if (_scrollViewer != null)
            {
                _scrollViewer.GotFocus += OnTextViewScrollGotFocus;
                _scrollViewer.ScrollChanged += OnTextViewScrollChanged;
                _scrollViewer.Bind(ScrollViewer.OffsetProperty, new Binding("SyncScrollOffset", BindingMode.OneWay));
            }

            TextArea.PointerWheelChanged += OnTextAreaPointerWheelChanged;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged -= OnTextViewScrollChanged;
                _scrollViewer.GotFocus -= OnTextViewScrollGotFocus;
                _scrollViewer = null;
            }

            TextArea.PointerWheelChanged -= OnTextAreaPointerWheelChanged;

            GC.Collect();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is ViewModels.TwoSideTextDiff diff)
            {
                var builder = new StringBuilder();
                var lines = IsOld ? diff.Old : diff.New;
                foreach (var line in lines)
                {
                    if (line.Content.Length > 10000)
                    {
                        builder.Append(line.Content.Substring(0, 1000));
                        builder.Append($"...({line.Content.Length - 1000} characters trimmed)");
                        builder.AppendLine();
                    }
                    else
                    {
                        builder.AppendLine(line.Content);
                    }
                }

                Text = builder.ToString();
            }
            else
            {
                Text = string.Empty;
            }
        }

        private void OnTextViewScrollGotFocus(object sender, GotFocusEventArgs e)
        {
            TrySetChunk(null);
        }

        private void OnTextViewScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (TextArea.IsFocused && DataContext is ViewModels.TwoSideTextDiff diff)
                diff.SyncScrollOffset = _scrollViewer.Offset;
        }

        private void OnTextAreaPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (!TextArea.IsFocused)
                Focus();
        }

        private ScrollViewer _scrollViewer = null;
    }

    public partial class TextDiffView : UserControl
    {
        public static readonly StyledProperty<bool> UseSideBySideDiffProperty =
            AvaloniaProperty.Register<TextDiffView, bool>(nameof(UseSideBySideDiff));

        public bool UseSideBySideDiff
        {
            get => GetValue(UseSideBySideDiffProperty);
            set => SetValue(UseSideBySideDiffProperty, value);
        }

        public static readonly StyledProperty<TextDiffViewChunk> SelectedChunkProperty =
            AvaloniaProperty.Register<TextDiffView, TextDiffViewChunk>(nameof(SelectedChunk));

        public TextDiffViewChunk SelectedChunk
        {
            get => GetValue(SelectedChunkProperty);
            set => SetValue(SelectedChunkProperty, value);
        }

        public static readonly StyledProperty<bool> IsUnstagedChangeProperty =
            AvaloniaProperty.Register<TextDiffView, bool>(nameof(IsUnstagedChange));

        public bool IsUnstagedChange
        {
            get => GetValue(IsUnstagedChangeProperty);
            set => SetValue(IsUnstagedChangeProperty, value);
        }

        public static readonly StyledProperty<bool> EnableChunkSelectionProperty =
            AvaloniaProperty.Register<TextDiffView, bool>(nameof(EnableChunkSelection));

        public bool EnableChunkSelection
        {
            get => GetValue(EnableChunkSelectionProperty);
            set => SetValue(EnableChunkSelectionProperty, value);
        }

        static TextDiffView()
        {
            UseSideBySideDiffProperty.Changed.AddClassHandler<TextDiffView>((v, _) =>
            {
                if (v.DataContext is Models.TextDiff diff)
                {
                    diff.SyncScrollOffset = Vector.Zero;

                    if (v.UseSideBySideDiff)
                        v.Editor.Content = new ViewModels.TwoSideTextDiff(diff);
                    else
                        v.Editor.Content = diff;
                }
            });

            SelectedChunkProperty.Changed.AddClassHandler<TextDiffView>((v, _) =>
            {
                var chunk = v.SelectedChunk;
                if (chunk == null)
                {
                    v.Popup.IsVisible = false;
                    return;
                }

                var top = chunk.Y + 16;
                var right = (chunk.Combined || !chunk.IsOldSide) ? 16 : v.Bounds.Width * 0.5f + 16;
                v.Popup.Margin = new Thickness(0, top, right, 0);
                v.Popup.IsVisible = true;
            });
        }

        public TextDiffView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (SelectedChunk != null)
                SetCurrentValue(SelectedChunkProperty, null);

            var diff = DataContext as Models.TextDiff;
            if (diff == null)
            {
                Editor.Content = null;
                GC.Collect();
                return;
            }

            if (UseSideBySideDiff)
                Editor.Content = new ViewModels.TwoSideTextDiff(diff, Editor.Content as ViewModels.TwoSideTextDiff);
            else
                Editor.Content = diff;

            IsUnstagedChange = diff.Option.IsUnstaged;
            EnableChunkSelection = diff.Option.WorkingCopyChange != null;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            if (SelectedChunk != null)
                SetCurrentValue(SelectedChunkProperty, null);
        }

        private void OnStageChunk(object sender, RoutedEventArgs e)
        {
            var chunk = SelectedChunk;
            if (chunk == null)
                return;

            var diff = DataContext as Models.TextDiff;
            if (diff == null)
                return;

            var change = diff.Option.WorkingCopyChange;
            if (change == null)
                return;

            var selection = diff.MakeSelection(chunk.StartIdx + 1, chunk.EndIdx + 1, chunk.Combined, chunk.IsOldSide);
            if (!selection.HasChanges)
                return;

            if (!selection.HasLeftChanges)
            {
                var workcopyView = this.FindAncestorOfType<WorkingCopy>();
                if (workcopyView == null)
                    return;

                var workcopy = workcopyView.DataContext as ViewModels.WorkingCopy;
                workcopy?.StageChanges(new List<Models.Change> { change });
            }
            else
            {
                var repoView = this.FindAncestorOfType<Repository>();
                if (repoView == null)
                    return;

                var repo = repoView.DataContext as ViewModels.Repository;
                if (repo == null)
                    return;

                repo.SetWatcherEnabled(false);

                var tmpFile = Path.GetTempFileName();
                if (change.WorkTree == Models.ChangeState.Untracked)
                {
                    diff.GenerateNewPatchFromSelection(change, null, selection, false, tmpFile);
                }
                else if (chunk.Combined)
                {
                    var treeGuid = new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).Result();
                    diff.GeneratePatchFromSelection(change, treeGuid, selection, false, tmpFile);
                }
                else
                {
                    var treeGuid = new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).Result();
                    diff.GeneratePatchFromSelectionSingleSide(change, treeGuid, selection, false, chunk.IsOldSide, tmpFile);
                }

                new Commands.Apply(diff.Repo, tmpFile, true, "nowarn", "--cache --index").Exec();
                File.Delete(tmpFile);

                repo.MarkWorkingCopyDirtyManually();
                repo.SetWatcherEnabled(true);
            }
        }

        private void OnUnstageChunk(object sender, RoutedEventArgs e)
        {
            var chunk = SelectedChunk;
            if (chunk == null)
                return;

            var diff = DataContext as Models.TextDiff;
            if (diff == null)
                return;

            var change = diff.Option.WorkingCopyChange;
            if (change == null)
                return;

            var selection = diff.MakeSelection(chunk.StartIdx + 1, chunk.EndIdx + 1, chunk.Combined, chunk.IsOldSide);
            if (!selection.HasChanges)
                return;

            if (!selection.HasLeftChanges)
            {
                var workcopyView = this.FindAncestorOfType<WorkingCopy>();
                if (workcopyView == null)
                    return;

                var workcopy = workcopyView.DataContext as ViewModels.WorkingCopy;
                workcopy?.UnstageChanges(new List<Models.Change> { change });
            }
            else
            {
                var repoView = this.FindAncestorOfType<Repository>();
                if (repoView == null)
                    return;

                var repo = repoView.DataContext as ViewModels.Repository;
                if (repo == null)
                    return;

                repo.SetWatcherEnabled(false);

                var treeGuid = new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).Result();
                var tmpFile = Path.GetTempFileName();
                if (change.Index == Models.ChangeState.Added)
                    diff.GenerateNewPatchFromSelection(change, treeGuid, selection, true, tmpFile);
                else if (chunk.Combined)
                    diff.GeneratePatchFromSelection(change, treeGuid, selection, true, tmpFile);
                else
                    diff.GeneratePatchFromSelectionSingleSide(change, treeGuid, selection, true, chunk.IsOldSide, tmpFile);

                new Commands.Apply(diff.Repo, tmpFile, true, "nowarn", "--cache --index --reverse").Exec();
                File.Delete(tmpFile);

                repo.MarkWorkingCopyDirtyManually();
                repo.SetWatcherEnabled(true);
            }
        }

        private void OnDiscardChunk(object sender, RoutedEventArgs e)
        {
            var chunk = SelectedChunk;
            if (chunk == null)
                return;

            var diff = DataContext as Models.TextDiff;
            if (diff == null)
                return;

            var change = diff.Option.WorkingCopyChange;
            if (change == null)
                return;

            var selection = diff.MakeSelection(chunk.StartIdx + 1, chunk.EndIdx + 1, chunk.Combined, chunk.IsOldSide);
            if (!selection.HasChanges)
                return;

            if (!selection.HasLeftChanges)
            {
                var workcopyView = this.FindAncestorOfType<WorkingCopy>();
                if (workcopyView == null)
                    return;

                var workcopy = workcopyView.DataContext as ViewModels.WorkingCopy;
                workcopy?.Discard(new List<Models.Change> { change }, diff.Option.IsUnstaged);
            }
            else
            {
                var repoView = this.FindAncestorOfType<Repository>();
                if (repoView == null)
                    return;

                var repo = repoView.DataContext as ViewModels.Repository;
                if (repo == null)
                    return;

                repo.SetWatcherEnabled(false);

                var tmpFile = Path.GetTempFileName();
                if (change.Index == Models.ChangeState.Added)
                {
                    diff.GenerateNewPatchFromSelection(change, null, selection, true, tmpFile);
                }
                else if (chunk.Combined)
                {
                    var treeGuid = new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).Result();
                    diff.GeneratePatchFromSelection(change, treeGuid, selection, true, tmpFile);
                }
                else
                {
                    var treeGuid = new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).Result();
                    diff.GeneratePatchFromSelectionSingleSide(change, treeGuid, selection, true, chunk.IsOldSide, tmpFile);
                }

                new Commands.Apply(diff.Repo, tmpFile, true, "nowarn", diff.Option.IsUnstaged ? "--reverse" : "--index --reverse").Exec();
                File.Delete(tmpFile);

                repo.MarkWorkingCopyDirtyManually();
                repo.SetWatcherEnabled(true);
            }
        }
    }
}
