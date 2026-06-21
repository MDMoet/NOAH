using System.ComponentModel;
using System.Windows.Input;
using Client.Services;
using Client.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace Client.Views;

/// <summary>
/// Displays the responsive calendar page with shared NOAH navigation.
/// </summary>
public class CalendarAdaptiveView : ContentView
{
    private const double DesktopFullCalendarCellSize = 150;

    private readonly CalendarViewModel _viewModel = ServiceHelper.GetRequiredService<CalendarViewModel>();
    private readonly Grid _desktopLayout;
    private readonly Grid _mobileLayout;
    private readonly DesktopNavigationView _desktopNavigation;
    private readonly MobileNavigationFooterView _mobileNavigation;
    private readonly Label _desktopMonthLabel;
    private readonly Label _mobileMonthLabel;
    private readonly Label _desktopAgendaTitleLabel;
    private readonly Label _mobileAgendaTitleLabel;
    private readonly Label _desktopTodayLabel;
    private readonly Label _mobileTodayLabel;
    private readonly Grid _desktopMiniCalendarGrid;
    private readonly Grid _desktopFullCalendarGrid;
    private readonly Grid _mobileCalendarGrid;

    /// <summary>
    /// Caches day cell borders per calendar grid so refreshes can update existing UI elements.
    /// </summary>
    private readonly Dictionary<Grid, List<Border>> _gridCellCache = new();
    private readonly VerticalStackLayout _desktopEventsStack;
    private readonly VerticalStackLayout _mobileEventsStack;
    private readonly ColumnDefinition _desktopSidebarColumn;
    private readonly Command _openSelectedDayReminderCommand;
    private readonly Command _openSelectedDayTaskCommand;

    /// <summary>
    /// Initializes the calendar layouts, navigation bindings, and calendar refresh events.
    /// </summary>
    public CalendarAdaptiveView()
    {
        BackgroundColor = GetColor("NoahBackground");

        _desktopNavigation = new DesktopNavigationView
        {
            ShowSettings = false,
            HeightRequest = 38
        };
        _desktopNavigation.SetBinding(DesktopNavigationView.IsCalendarSelectedProperty, new Binding("IsCalendarPageOpen"));

        _mobileNavigation = new MobileNavigationFooterView
        {
            IsCalendarSelected = true
        };
        _openSelectedDayReminderCommand = new Command(OpenReminderDialogForSelectedDay);
        _openSelectedDayTaskCommand = new Command(OpenTaskDialogForSelectedDay);
        BindNavigationCommands();

        _desktopMonthLabel = BuildMonthLabel(12);
        _mobileMonthLabel = BuildMonthLabel(13);
        _desktopAgendaTitleLabel = BuildAgendaTitleLabel(13);
        _mobileAgendaTitleLabel = BuildAgendaTitleLabel(10);
        _desktopTodayLabel = BuildTodayDateLabel();
        _mobileTodayLabel = BuildTodayDateLabel();
        _desktopMiniCalendarGrid = BuildCalendarGrid();
        _desktopFullCalendarGrid = BuildCalendarGrid();
        _desktopFullCalendarGrid.RowSpacing = 10;
        _desktopFullCalendarGrid.ColumnSpacing = 10;
        _desktopFullCalendarGrid.WidthRequest = GetDesktopFullCalendarWidth();
        _desktopFullCalendarGrid.HorizontalOptions = LayoutOptions.Center;
        _mobileCalendarGrid = BuildCalendarGrid();
        _desktopEventsStack = new VerticalStackLayout { Spacing = 8 };
        _mobileEventsStack = new VerticalStackLayout { Spacing = 6 };
        _desktopSidebarColumn = new ColumnDefinition(320);

        _desktopLayout = BuildDesktopLayout();
        _mobileLayout = BuildMobileLayout();

        Grid root = new();
        root.Children.Add(_desktopLayout);
        root.Children.Add(_mobileLayout);
        Content = root;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _desktopMiniCalendarGrid.SizeChanged += (s, e) => AdjustGridCellsToSquare(_desktopMiniCalendarGrid);
        _mobileCalendarGrid.SizeChanged += (s, e) => AdjustGridCellsToSquare(_mobileCalendarGrid);
        _desktopFullCalendarGrid.SizeChanged += (s, e) => AdjustGridCellsToSquare(_desktopFullCalendarGrid);

        SizeChanged += OnSizeChanged;
        BindingContextChanged += OnCalendarBindingContextChanged;

        ApplyNavigationContext();
        RefreshCalendar();
        RefreshEvents();
        UpdateLayoutMode();
    }

    private void OnCalendarBindingContextChanged(object? sender, EventArgs e)
        => ApplyNavigationContext();

    private void BindNavigationCommands()
    {
        _desktopNavigation.SetBinding(DesktopNavigationView.NavigateHomeCommandProperty, new Binding("NavigateHomeCommand"));
        _desktopNavigation.SetBinding(DesktopNavigationView.NavigateMileageCommandProperty, new Binding("NavigateMileageCommand"));
        _desktopNavigation.SetBinding(DesktopNavigationView.NavigateNotesCommandProperty, new Binding("NavigateNotesCommand"));
        _desktopNavigation.SetBinding(DesktopNavigationView.NavigateCalendarCommandProperty, new Binding("NavigateCalendarCommand"));
        _desktopNavigation.SetBinding(DesktopNavigationView.NavigateChatCommandProperty, new Binding("NavigateChatCommand"));
        _desktopNavigation.SetBinding(DesktopNavigationView.ToggleSettingsCommandProperty, new Binding("ToggleSettingsCommand"));

        _mobileNavigation.SetBinding(MobileNavigationFooterView.NavigateHomeCommandProperty, new Binding("NavigateHomeCommand"));
        _mobileNavigation.SetBinding(MobileNavigationFooterView.NavigateMileageCommandProperty, new Binding("NavigateMileageCommand"));
        _mobileNavigation.SetBinding(MobileNavigationFooterView.NavigateNotesCommandProperty, new Binding("NavigateNotesCommand"));
        _mobileNavigation.SetBinding(MobileNavigationFooterView.NavigateCalendarCommandProperty, new Binding("NavigateCalendarCommand"));
        _mobileNavigation.SetBinding(MobileNavigationFooterView.NavigateChatCommandProperty, new Binding("NavigateChatCommand"));
    }

    private void ApplyNavigationContext()
    {
        _desktopNavigation.BindingContext = BindingContext;
        _mobileNavigation.BindingContext = BindingContext;

        if (BindingContext is HomeViewModel homeViewModel)
        {
            _desktopSidebarColumn.Width = homeViewModel.DesktopHomeSidebarWidth;
            homeViewModel.PropertyChanged -= OnHomeViewModelPropertyChanged;
            homeViewModel.PropertyChanged += OnHomeViewModelPropertyChanged;
        }
    }

    private void OnHomeViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is HomeViewModel homeViewModel &&
            e.PropertyName == nameof(HomeViewModel.DesktopHomeSidebarWidth))
        {
            _desktopSidebarColumn.Width = homeViewModel.DesktopHomeSidebarWidth;
        }
    }

    private void OnSizeChanged(object? sender, EventArgs e)
        => UpdateLayoutMode();

    private void UpdateLayoutMode()
    {
        bool isDesktop = Width >= 720;
        _desktopLayout.IsVisible = isDesktop;
        _mobileLayout.IsVisible = !isDesktop;
    }

    private Grid BuildDesktopLayout()
    {
        Grid root = new()
        {
            ColumnDefinitions =
            {
                _desktopSidebarColumn,
                new ColumnDefinition(6),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(220)
            }
        };

        Grid sidebar = new()
        {
            BackgroundColor = GetColor("NoahSurface"),
            Padding = new Thickness(12, 14),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        sidebar.Add(_desktopNavigation, 0, 0);
        sidebar.Add(BuildMonthHeader(_desktopMonthLabel, true), 0, 1);
        sidebar.Add(BuildWeekdayHeader(10), 0, 2);
        sidebar.Add(_desktopMiniCalendarGrid, 0, 3);
        sidebar.Add(BuildActionButtons(36, 20), 0, 4);

        root.Add(sidebar, 0, 0);
        root.Add(new BoxView { Color = Color.FromArgb("#171126"), WidthRequest = 6 }, 1, 0);
        root.Add(BuildFullCalendarPanel(), 2, 0);
        root.Add(BuildDesktopAgendaPanel(), 3, 0);

        return root;
    }

    private Grid BuildMobileLayout()
    {
        Grid root = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        ScrollView scrollView = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(12, 20, 12, 16),
                Spacing = 12,
                Children =
                {
                    BuildMonthHeader(_mobileMonthLabel, true),
                    BuildWeekdayHeader(10),
                    _mobileCalendarGrid,
                    BuildMobileAgendaPanel(),
                    BuildActionButtons(36, 20)
                }
            }
        };

        root.Add(scrollView, 0, 0);
        root.Add(_mobileNavigation, 0, 1);

        return root;
    }

    private Grid BuildFullCalendarPanel()
    {
        Grid panel = new()
        {
            Padding = new Thickness(16, 14),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star)
            }
        };

        Grid calendarContent = new()
        {
            WidthRequest = GetDesktopFullCalendarWidth(),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            RowDefinitions =
            {
                new RowDefinition(28),
                new RowDefinition(GridLength.Auto)
            }
        };

        calendarContent.Add(BuildWeekdayHeader(20), 0, 0);
        calendarContent.Add(_desktopFullCalendarGrid, 0, 1);
        panel.Add(calendarContent, 0, 0);

        return panel;
    }

    private Border BuildDesktopAgendaPanel()
    {
        Grid content = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };

        content.Add(BuildAgendaHeader(_desktopAgendaTitleLabel, _desktopTodayLabel), 0, 0);
        content.Add(_desktopEventsStack, 0, 1);

        return new Border
        {
            Style = BuildPanelStyle(),
            Margin = new Thickness(0, 14, 14, 14),
            Padding = new Thickness(12, 14),
            Content = content
        };
    }

    private Border BuildMobileAgendaPanel()
    {
        Grid content = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };

        content.Add(BuildAgendaHeader(_mobileAgendaTitleLabel, _mobileTodayLabel), 0, 0);
        content.Add(_mobileEventsStack, 0, 1);

        return new Border
        {
            Style = BuildPanelStyle(),
            HeightRequest = 184,
            Padding = new Thickness(10, 12),
            Content = content
        };
    }

    private Grid BuildMonthHeader(Label monthLabel, bool showMonthControls)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            Margin = new Thickness(0, 8, 0, 10)
        };

        Image todayIcon = new()
        {
            Source = "calendar_purple.png",
            WidthRequest = 19,
            HeightRequest = 19,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center
        };
        todayIcon.GestureRecognizers.Add(new TapGestureRecognizer { Command = _viewModel.TodayCommand });

        HorizontalStackLayout monthTitle = new()
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                todayIcon,
                monthLabel
            }
        };

        grid.Add(monthTitle, 1, 0);

        if (showMonthControls)
        {
            Border previousButton = BuildMonthButton("<", _viewModel.PrevMonthCommand);
            Border nextButton = BuildMonthButton(">", _viewModel.NextMonthCommand);

            grid.Add(previousButton, 0, 0);
            grid.Add(nextButton, 2, 0);
        }

        return grid;
    }

    private static Border BuildMonthButton(string text, ICommand command)
    {
        Border button = new()
        {
            BackgroundColor = GetColor("NoahCard"),
            Stroke = GetColor("NoahAccent"),
            StrokeThickness = 0.5,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            WidthRequest = 26,
            HeightRequest = 26,
            Content = new Label
            {
                Text = text,
                FontSize = 16,
                TextColor = GetColor("NoahAccent"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        button.GestureRecognizers.Add(new TapGestureRecognizer { Command = command });
        return button;
    }

    private static Label BuildMonthLabel(double fontSize)
        => new()
        {
            FontSize = fontSize,
            FontFamily = "OpenSansSemibold",
            FontAttributes = FontAttributes.Bold,
            TextColor = GetColor("NoahAccent"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };

    private static Grid BuildWeekdayHeader(double fontSize)
    {
        Grid grid = new()
        {
            Margin = new Thickness(0, 0, 0, 5)
        };

        for (int column = 0; column < 7; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        string[] days = ["M", "T", "W", "T", "F", "S", "S"];

        for (int i = 0; i < days.Length; i++)
        {
            grid.Add(new Label
            {
                Text = days[i],
                FontSize = fontSize,
                TextColor = GetColor("NoahAccent"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }, i, 0);
        }

        return grid;
    }

    private static Grid BuildAgendaHeader(Label titleLabel, Label dateLabel)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(0, 0, 0, 12)
        };

        grid.Add(titleLabel, 0, 0);

        grid.Add(dateLabel, 1, 0);
        return grid;
    }

    private static Label BuildAgendaTitleLabel(double titleSize)
        => new()
        {
            FontSize = titleSize,
            FontFamily = "OpenSansSemibold",
            FontAttributes = FontAttributes.Bold,
            TextColor = GetColor("NoahAccent")
        };

    private static Label BuildTodayDateLabel()
        => new()
        {
            FontSize = 9,
            TextColor = GetColor("NoahAccent"),
            VerticalOptions = LayoutOptions.Center
        };

    private HorizontalStackLayout BuildActionButtons(double size, double iconSize)
    {
        return new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                BuildActionButton("bell_badge_outline.png", size, iconSize, _openSelectedDayReminderCommand),
                BuildActionButton("checkbox_marked_circle_plus_outline.png", size, iconSize, _openSelectedDayTaskCommand)
            }
        };
    }

    private static Border BuildActionButton(string icon, double size, double iconSize, ICommand command)
    {
        Border button = new()
        {
            BackgroundColor = GetColor("NoahAccent"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            WidthRequest = size,
            HeightRequest = size,
            Content = new Image
            {
                Source = icon,
                WidthRequest = iconSize,
                HeightRequest = iconSize,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        button.GestureRecognizers.Add(new TapGestureRecognizer { Command = command });
        return button;
    }

    private void OpenReminderDialogForSelectedDay()
    {
        if (BindingContext is HomeViewModel homeViewModel)
        {
            homeViewModel.OpenReminderDialogForDate(_viewModel.SelectedDay?.Date ?? DateTime.Today);
        }
    }

    private void OpenTaskDialogForSelectedDay()
    {
        if (BindingContext is HomeViewModel homeViewModel)
        {
            homeViewModel.OpenTaskDialogForDate(_viewModel.SelectedDay?.Date ?? DateTime.Today);
        }
    }

    private static Grid BuildCalendarGrid()
    {
        Grid grid = new()
        {
            RowSpacing = 4,
            ColumnSpacing = 4,
            VerticalOptions = LayoutOptions.Start
        };

        for (int column = 0; column < 7; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int row = 0; row < 6; row++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        return grid;
    }

    private static double GetDesktopFullCalendarWidth()
        => DesktopFullCalendarCellSize * 7 + 10 * 6;

    /// <summary>
    /// Synchronizes visible calendar labels and grids when the calendar view model changes.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarViewModel.MonthYearLabel) ||
            e.PropertyName == nameof(CalendarViewModel.TodayLabel) ||
            e.PropertyName == nameof(CalendarViewModel.SelectedDayHeading))
        {
            _desktopMonthLabel.Text = _viewModel.MonthYearLabel;
            _mobileMonthLabel.Text = _viewModel.MonthYearLabel;
            _desktopAgendaTitleLabel.Text = _viewModel.SelectedDayHeading;
            _mobileAgendaTitleLabel.Text = _viewModel.SelectedDayHeading;
            _desktopTodayLabel.Text = _viewModel.TodayLabel;
            _mobileTodayLabel.Text = _viewModel.TodayLabel;
            return;
        }

        if (e.PropertyName == nameof(CalendarViewModel.SelectedDay) ||
            e.PropertyName == nameof(CalendarViewModel.FullCalendarDays) ||
            e.PropertyName == nameof(CalendarViewModel.TodayEvents))
        {
            RefreshCalendar();
            RefreshEvents();
        }
    }

    /// <summary>
    /// Refreshes calendar labels, day cells, and square cell sizing for all active layouts.
    /// </summary>
    private void RefreshCalendar()
    {
        _desktopMonthLabel.Text = _viewModel.MonthYearLabel;
        _mobileMonthLabel.Text = _viewModel.MonthYearLabel;
        _desktopAgendaTitleLabel.Text = _viewModel.SelectedDayHeading;
        _mobileAgendaTitleLabel.Text = _viewModel.SelectedDayHeading;
        _desktopTodayLabel.Text = _viewModel.TodayLabel;
        _mobileTodayLabel.Text = _viewModel.TodayLabel;

        PopulateCalendarGrid(_desktopMiniCalendarGrid, _viewModel.MiniCalendarDays, _viewModel.FirstDayColumn, 24, false);
        PopulateCalendarGrid(_mobileCalendarGrid, _viewModel.MiniCalendarDays, _viewModel.FirstDayColumn, 27, false);
        PopulateCalendarGrid(_desktopFullCalendarGrid, _viewModel.FullCalendarDays, _viewModel.FirstDayColumn, DesktopFullCalendarCellSize, true);

        AdjustGridCellsToSquare(_desktopMiniCalendarGrid);
        AdjustGridCellsToSquare(_mobileCalendarGrid);
        AdjustGridCellsToSquare(_desktopFullCalendarGrid);
    }

    /// <summary>
    /// Sizes day cells to match the available grid width and keep calendar cells square.
    /// </summary>
    /// <param name="grid">The calendar grid to resize.</param>
    private void AdjustGridCellsToSquare(Grid grid)
    {
        bool isDesktopFullCalendar = ReferenceEquals(grid, _desktopFullCalendarGrid);
        if (grid.Width <= 0 && !isDesktopFullCalendar)
            return;

        double totalSpacingX = grid.ColumnSpacing * (grid.ColumnDefinitions.Count - 1);
        double availableWidth = Math.Max(0, grid.Width - totalSpacingX);
        double cellSize = isDesktopFullCalendar
            ? DesktopFullCalendarCellSize
            : Math.Floor(availableWidth / Math.Max(1, grid.ColumnDefinitions.Count));
        if (isDesktopFullCalendar)
        {
            grid.WidthRequest = GetDesktopFullCalendarWidth();

            for (int c = 0; c < grid.ColumnDefinitions.Count; c++)
                grid.ColumnDefinitions[c].Width = new GridLength(cellSize);
        }

        for (int r = 0; r < grid.RowDefinitions.Count; r++)
            grid.RowDefinitions[r].Height = new GridLength(cellSize);

        grid.HeightRequest = cellSize * grid.RowDefinitions.Count + grid.RowSpacing * (grid.RowDefinitions.Count - 1);

        if (_gridCellCache.TryGetValue(grid, out var cached))
        {
            foreach (var cell in cached)
            {
                cell.HeightRequest = cellSize;
                cell.WidthRequest = cellSize;
            }
        }
        else
        {
            foreach (var child in grid.Children.OfType<Border>())
            {
                child.HeightRequest = cellSize;
                child.WidthRequest = cellSize;
            }
        }
    }

    /// <summary>
    /// Populates a calendar grid by updating cached day cells or rebuilding them when the month shape changes.
    /// </summary>
    /// <param name="grid">The grid that receives the day cells.</param>
    /// <param name="days">The calendar days to render.</param>
    /// <param name="firstDayColumn">The starting column for the first day.</param>
    /// <param name="cellHeight">The fallback cell height before square sizing is applied.</param>
    /// <param name="fullSize">Whether to render full-size desktop day cells.</param>
    private void PopulateCalendarGrid(Grid grid, IEnumerable<CalendarDay> days, int firstDayColumn, double cellHeight, bool fullSize)
    {
        var dayList = days.ToList();

        if (!_gridCellCache.TryGetValue(grid, out var cached))
            cached = new List<Border>();

        if (cached.Count == dayList.Count)
        {
            int index = firstDayColumn;
            for (int i = 0; i < dayList.Count; i++)
            {
                var day = dayList[i];
                var cell = cached[i];
                UpdateDayCell(cell, day, cellHeight, fullSize);
                Grid.SetColumn(cell, index % 7);
                Grid.SetRow(cell, index / 7);
                index++;
            }

            if (grid.Children.Count != cached.Count)
            {
                grid.Children.Clear();
                foreach (var c in cached)
                    grid.Children.Add(c);
            }
        }
        else
        {
            cached.Clear();
            grid.Children.Clear();

            int index = firstDayColumn;
            foreach (CalendarDay day in dayList)
            {
                Border cell = BuildDayCell(day, cellHeight, fullSize);
                cached.Add(cell);
                grid.Add(cell, index % 7, index / 7);
                index++;
            }

            _gridCellCache[grid] = cached;
        }
    }

    /// <summary>
    /// Updates an existing day cell without recreating its visual tree.
    /// </summary>
    /// <param name="cell">The day cell to update.</param>
    /// <param name="day">The day data to display.</param>
    /// <param name="height">The fallback cell height.</param>
    /// <param name="fullSize">Whether the cell is rendered in the full desktop calendar.</param>
    private void UpdateDayCell(Border cell, CalendarDay day, double height, bool fullSize)
    {
        cell.BackgroundColor = ResolveDayBackground(day);
        cell.StrokeThickness = day.IsToday || day.IsSelected ? 0.9 : 0.55;
        cell.StrokeShape = new RoundRectangle { CornerRadius = fullSize ? 8 : 7 };
        cell.Padding = fullSize ? new Thickness(14, 10) : new Thickness(0);

        var tap = cell.GestureRecognizers.OfType<TapGestureRecognizer>().FirstOrDefault();
        if (tap != null)
        {
            tap.Command = _viewModel.SelectDayCommand;
            tap.CommandParameter = day;
        }

        if (cell.Content is Grid content && content.Children.Count >= 2)
        {
            if (content.Children[0] is Label lbl)
            {
                lbl.Text = day.DayNumber;
                lbl.FontSize = fullSize ? 15 : 8;
                lbl.TextColor = day.IsToday ? Colors.White : GetColor("NoahAccent");
                lbl.Opacity = day.IsCurrentMonth ? 1 : 0.45;
                lbl.FontAttributes = day.IsToday ? FontAttributes.Bold : FontAttributes.None;
                lbl.HorizontalOptions = fullSize ? LayoutOptions.Start : LayoutOptions.Center;
            }

            if (content.Children[1] is Border dot)
            {
                dot.IsVisible = day.HasEvents;
                dot.BackgroundColor = day.IsToday ? Colors.Black : GetColor("NoahAccent");
                dot.WidthRequest = fullSize ? 7 : 3;
                dot.HeightRequest = fullSize ? 7 : 3;
            }
        }
    }

    private Border BuildDayCell(CalendarDay day, double height, bool fullSize)
    {
        Border cell = new()
        {
            BackgroundColor = ResolveDayBackground(day),
            Stroke = GetColor("NoahAccent"),
            StrokeThickness = day.IsToday || day.IsSelected ? 0.9 : 0.55,
            StrokeShape = new RoundRectangle { CornerRadius = fullSize ? 8 : 7 },
            HeightRequest = height,
            WidthRequest = height,
            Padding = fullSize ? new Thickness(14, 10) : new Thickness(0)
        };

        cell.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = _viewModel.SelectDayCommand,
            CommandParameter = day
        });

        Grid content = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        content.Add(new Label
        {
            Text = day.DayNumber,
            FontSize = fullSize ? 15 : 8,
            TextColor = day.IsToday ? Colors.White : GetColor("NoahAccent"),
            Opacity = day.IsCurrentMonth ? 1 : 0.45,
            FontAttributes = day.IsToday ? FontAttributes.Bold : FontAttributes.None,
            HorizontalOptions = fullSize ? LayoutOptions.Start : LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);

        content.Add(new Border
        {
            BackgroundColor = day.IsToday ? Colors.Black : GetColor("NoahAccent"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            WidthRequest = fullSize ? 7 : 3,
            HeightRequest = fullSize ? 7 : 3,
            IsVisible = day.HasEvents,
            HorizontalOptions = fullSize ? LayoutOptions.End : LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = fullSize ? new Thickness(0, 0, 1, 1) : new Thickness(0, 0, 0, 3)
        }, 0, 2);

        cell.Content = content;
        return cell;
    }

    private static Color ResolveDayBackground(CalendarDay day)
    {
        if (day.IsToday)
            return GetColor("NoahAccent");

        if (day.IsSelected)
            return GetColor("NoahCardSelected");

        return day.IsCurrentMonth ? Color.FromArgb("#080610") : Colors.Transparent;
    }

    private void RefreshEvents()
    {
        _desktopEventsStack.Children.Clear();
        _mobileEventsStack.Children.Clear();

        foreach (CalendarEvent calendarEvent in _viewModel.TodayEvents)
        {
            _desktopEventsStack.Children.Add(BuildEventCard(calendarEvent, false));
            _mobileEventsStack.Children.Add(BuildEventCard(calendarEvent, true));
        }
    }

    private static Border BuildEventCard(CalendarEvent calendarEvent, bool compact)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(compact ? 36 : 42),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };

        grid.Add(new Label
        {
            Text = calendarEvent.TimeDisplay,
            FontSize = compact ? 7 : 8,
            TextColor = GetColor("NoahAccent"),
            VerticalOptions = LayoutOptions.Start
        }, 0, 0);

        grid.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = calendarEvent.Title,
                    FontSize = compact ? 8 : 9,
                    FontFamily = "OpenSansSemibold",
                    FontAttributes = FontAttributes.Bold,
                    TextColor = GetColor("NoahAccent")
                },
                new Label
                {
                    Text = calendarEvent.Subtitle,
                    FontSize = compact ? 7 : 8,
                    TextColor = GetColor("NoahTextSecondary")
                }
            }
        }, 1, 0);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#0A0712"),
            Stroke = GetColor("NoahAccent"),
            StrokeThickness = 0.6,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = compact ? new Thickness(10, 7) : new Thickness(10, 8),
            Content = grid
        };
    }

    private static Style BuildPanelStyle()
        => new(typeof(Border))
        {
            Setters =
            {
                new Setter { Property = Border.BackgroundColorProperty, Value = GetColor("NoahCard") },
                new Setter { Property = Border.StrokeProperty, Value = GetColor("NoahAccent") },
                new Setter { Property = Border.StrokeThicknessProperty, Value = 0.7 },
                new Setter { Property = Border.StrokeShapeProperty, Value = new RoundRectangle { CornerRadius = 10 } }
            }
        };

    private static Color GetColor(string key)
        => Application.Current?.Resources.TryGetValue(key, out object value) == true && value is Color color
            ? color
            : Colors.Transparent;
}
