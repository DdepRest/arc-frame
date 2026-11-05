# Handoff Report: Survey Investigation for R1 and R2

## 1. Observation

### R1.1: Right Sidebar in CalculationView
- **File**: `MosquitoNetCalculator/MainWindow.xaml`, lines 204–301:
  ```xml
  <!-- PRIMARY: РАСЧЁТ view -->
  <Grid x:Name="CalculationView" Background="{DynamicResource AppBg}" Margin="{DynamicResource Gap.CalculationView}">
      <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>

      <Grid Grid.Column="0" Margin="{DynamicResource Gap.Window}">
          <!-- Action bar, Quick-Add, Table, Total, Status bar -->
          ...
      </Grid>

      <!-- Docked right sidebar: Open by default without modal backdrop -->
      <Border x:Name="SidebarPanel" Grid.Column="1"
              Width="340"
              Background="{DynamicResource Surface}"
              BorderBrush="{DynamicResource Border}"
              BorderThickness="1,0,0,0"
              Visibility="Visible">
          <Grid>
              <Grid.RowDefinitions>
                  <RowDefinition Height="Auto"/>
                  <RowDefinition Height="*"/>
              </Grid.RowDefinitions>
              <Border Grid.Row="0" Style="{StaticResource OverlayHeader}">
                  <Grid>
                      <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                          <TextBlock Text="&#xE77B;" FontFamily="{DynamicResource Font.Icon}" FontSize="{StaticResource Type.Subtitle}"
                                     Foreground="{DynamicResource AccentText}" VerticalAlignment="Center" Margin="{DynamicResource Gap.Inline}"/>
                          <TextBlock Text="Данные заказа" FontSize="{StaticResource Type.BodyLg}" FontWeight="SemiBold"
                                     Foreground="{DynamicResource TextPrimary}" VerticalAlignment="Center"/>
                      </StackPanel>
                      <Button Style="{StaticResource OverlayCloseButton}"
                              HorizontalAlignment="Right" Click="CloseSidebar_Click"/>
                  </Grid>
              </Border>
              <controls:SidebarControl x:Name="SidebarControl" Grid.Row="1"/>
          </Grid>
      </Border>
  </Grid>
  ```
- **File**: `MosquitoNetCalculator/Controls/ActionBarControl.xaml`, lines 191–198:
  ```xml
  <Button Style="{DynamicResource ClientInfoButton}"
          Click="BtnOrderInfo_Click" Margin="{DynamicResource Gap.Inline}"
          ToolTip="Заполните: имя, телефон, адрес, номер договора, примечания">
      <StackPanel Orientation="Horizontal">
          <TextBlock Style="{DynamicResource FluentIconButton}" Text="&#xE77B;"/>
          <TextBlock Text="Заказчик"/>
      </StackPanel>
  </Button>
  ```
- **File**: `MosquitoNetCalculator/Controls/ActionBarControl.xaml.cs`, lines 186–190:
  ```csharp
  private void BtnOrderInfo_Click(object sender, RoutedEventArgs e)
  {
      if (!TryGetMainWindow(nameof(BtnOrderInfo_Click), out var mw)) return;
      mw.ToggleSidebarOverlay();
  }
  ```
- **File**: `MosquitoNetCalculator/MainWindow.xaml.cs`, lines 673–689:
  ```csharp
  internal void ToggleSidebarOverlay()
  {
      if (SidebarPanel.Visibility == Visibility.Visible)
      {
          SidebarPanel.Visibility = Visibility.Collapsed;
      }
      else
      {
          SidebarPanel.Visibility = Visibility.Visible;
          Sidebar.ResetScroll();
      }
  }

  private void CloseSidebar_Click(object sender, RoutedEventArgs e)
  {
      SidebarPanel.Visibility = Visibility.Collapsed;
  }
  ```
- **Overlay Pattern in MainWindow.xaml**: lines 306–591 define existing overlays (`OrdersOverlay`, `PricesOverlay`, `UpdatesOverlay`, `AdminOverlay`, `SlopeOverlay`, `AiOverlay`, `PrintOverlay`) as overlay `Grid` elements at `Panel.ZIndex="15"`, with a scrim backdrop and slide transform:
  ```xml
  <Grid x:Name="OrdersOverlay" Visibility="Collapsed" Panel.ZIndex="15">
      <Border x:Name="OrdersBackdrop" Background="{DynamicResource Scrim}" Opacity="0"
              MouseLeftButtonDown="Backdrop_MouseLeftButtonDown" Cursor="Hand"/>
      <Border x:Name="OrdersPanel" Background="{DynamicResource Surface}" ...>
          <Border.RenderTransform>
              <TranslateTransform x:Name="OrdersSlideTransform" X="0"/>
          </Border.RenderTransform>
          ...
      </Border>
  </Grid>
  ```
- **Overlay Registration**: In `MosquitoNetCalculator/MainWindow.xaml.cs`, lines 382–395:
  ```csharp
  _ordersEntry   = new OverlayManager.OverlayEntry(OrdersOverlay,   OrdersPanel,   OrdersBackdrop,   OrdersSlideTransform);
  _pricesEntry   = new OverlayManager.OverlayEntry(PricesOverlay,   PricesPanel,   PricesBackdrop,   PricesSlideTransform);
  _updatesEntry  = new OverlayManager.OverlayEntry(UpdatesOverlay,  UpdatesPanel,  UpdatesBackdrop,  UpdatesSlideTransform);
  _printEntry    = new OverlayManager.OverlayEntry(PrintOverlay,    PrintPanel,    PrintBackdrop,    PrintSlideTransform);
  _adminEntry    = new OverlayManager.OverlayEntry(AdminOverlay,    AdminPanel,    AdminBackdrop,    AdminSlideTransform);
  var slopeEntry = new OverlayManager.OverlayEntry(SlopeOverlay,    SlopePanel,    SlopeBackdrop,    SlopeSlideTransform);

  _overlayManager = new OverlayManager(
      new[] { _ordersEntry, _pricesEntry, _updatesEntry, _printEntry, _adminEntry, slopeEntry },
      SetActiveNavButton,
      SavePrintSettings);
  ```

---

### R1.2: Left Navigation Panel (NavPanel) Expansion
- **File**: `MosquitoNetCalculator/MainWindow.xaml`, lines 594–605:
  ```xml
  <!-- NavigationView (fixed expanded panel; optionally collapsible) -->
  <Border x:Name="NavPanel"
          HorizontalAlignment="Left" Width="160"
          Panel.ZIndex="16"
          Background="{DynamicResource SidebarBg}"
          BorderBrush="{DynamicResource Border}" BorderThickness="0,0,1,0"
          ClipToBounds="True"
          WindowChrome.IsHitTestVisibleInChrome="True">
      <StackPanel VerticalAlignment="Top" Margin="{DynamicResource Gap.NavCluster}">
          <Button x:Name="BtnToggleNav" Style="{StaticResource NavButton}"
                  ToolTip="Свернуть панель навигации" Click="BtnToggleNav_Click" ...>
  ```
- **File**: `MosquitoNetCalculator/Services/NavigationService.cs`, line 31:
  `public bool IsExpanded { get; private set; } = true;`
- **File**: `MosquitoNetCalculator/MainWindow.xaml.cs`, lines 375–380 in `InitializeServices()`:
  ```csharp
  // Рекомендованный режим Studio: компактный иконочный рельс (52px) по умолчанию
  _navService.Collapse();
  CalculationView.Margin = new Thickness(52, 0, 0, 0);
  NavPanel.Width = 52;
  NavToggleLabel.Visibility = Visibility.Collapsed;
  BtnToggleNav.ToolTip = "Развернуть панель навигации";
  ```
- **File**: `MosquitoNetCalculator/MainWindow.xaml.cs`, lines 654–667:
  ```csharp
  private void BtnToggleNav_Click(object sender, RoutedEventArgs e)
  {
      if (_navService == null) return;

      _navService.Toggle();
      bool expanded = _navService.IsExpanded;

      UpdateCalculationViewMargin(animated: true);

      NavToggleLabel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
      BtnToggleNav.ToolTip = expanded
          ? "Свернуть панель навигации"
          : "Развернуть панель навигации";
  }
  ```

---

### R2: Segmented Pill Tabs for Orders History
- **File**: `MosquitoNetCalculator/Controls/OrdersHistoryControl.xaml`, lines 23–34:
  ```xml
  <!-- Segmented View Toggle: [ Все заказы ] [ 🗑 Корзина (N) ] -->
  <Border Background="{DynamicResource SurfaceVariant}" CornerRadius="{DynamicResource Radius.Capsule}"
          Padding="{DynamicResource Pad.ControlSm}" Margin="{DynamicResource Gap.InlineLg}" BorderBrush="{DynamicResource Border}" BorderThickness="1">
      <StackPanel Orientation="Horizontal">
          <RadioButton x:Name="RbActiveOrders" Content="Все заказы"
                       IsChecked="True" Checked="RbViewMode_Checked"
                       Style="{DynamicResource SegmentedRadioButton}"/>
          <RadioButton x:Name="RbTrashOrders" Content="🗑 Корзина"
                       Checked="RbViewMode_Checked"
                       Style="{DynamicResource SegmentedRadioButton}"/>
      </StackPanel>
  </Border>
  ```
- **File**: `MosquitoNetCalculator/Themes/InputStyles.RadioButton.xaml`, lines 8–35 defines default implicit `Style TargetType="RadioButton"`:
  ```xml
  <Border x:Name="Outer" Width="18" Height="18" CornerRadius="{DynamicResource Radius.Pill}"
          Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Border}" BorderThickness="2" ...>
      <Ellipse x:Name="Dot" Width="10" Height="10" Fill="{DynamicResource Accent}" Visibility="Collapsed" .../>
  </Border>
  ```
- **Search for `SegmentedRadioButton`**: `grep_search` across entire workspace returned zero definitions of `SegmentedRadioButton` in any file. Because the resource does not exist, WPF silently falls back to the default implicit `RadioButton` style, which draws the Windows-style circular radio button and dot.
- **Design Token Definitions**:
  - `Radius.Control`: `MosquitoNetCalculator/Themes/Tokens.Radius.xaml` line 46:
    `<CornerRadius x:Key="Radius.Control">8</CornerRadius>`
  - `Pad.ButtonSm`: `MosquitoNetCalculator/Themes/Tokens.Spacing.xaml` line 187:
    `<Thickness x:Key="Pad.ButtonSm">10,3</Thickness>`
  - `Accent`: `MosquitoNetCalculator/Themes/Brushes.xaml` line 9 (`#005FB8`) and `ThemeService.cs` line 305 (`#3574C3`).
  - `OnAccent`: `MosquitoNetCalculator/Themes/Brushes.xaml` line 14 (`#FFFFFF`).
  - `SurfaceVariant`: Missing from `Themes/Brushes.xaml` and `Services/ThemeService.cs` (though referenced in `OrdersHistoryControl.xaml`, `TotalCardControl.xaml`, `DraftItemsPickerDialog.xaml`, and `SettingsWindow.xaml`).

---

## 2. Logic Chain

### R1.1: Restoring SidebarOverlay
1. *Observation*: `SidebarPanel` is currently placed as column 1 inside `CalculationView`. This forces `CalculationView` to define `<ColumnDefinition Width="Auto"/>` and causes `SidebarPanel` to steal 340px of horizontal layout space when visible.
2. *Observation*: All other flyouts (`OrdersOverlay`, `PricesOverlay`, `UpdatesOverlay`, `SlopeOverlay`, etc.) reside in the parent overlay container at `Panel.ZIndex="15"`, equipped with `TranslateTransform` for smooth slide-in/out and a scrim `Border` for backdrop dismissal.
3. *Observation*: `OverlayManager` already encapsulates show/hide slide animations, backdrop opacity transitions, and mutex behavior for overlays using `OverlayManager.OverlayEntry`.
4. *Deduction*:
   - Moving `SidebarPanel` out of `CalculationView` into a dedicated `<Grid x:Name="SidebarOverlay" Visibility="Collapsed" Panel.ZIndex="15">` completely removes the 340px deduction from `CalculationView`.
   - `CalculationView` can drop `<ColumnDefinition Width="Auto"/>` and `<Grid.ColumnDefinitions>`, taking 100% of available width.
   - Creating `_sidebarEntry = new OverlayManager.OverlayEntry(SidebarOverlay, SidebarPanel, SidebarBackdrop, SidebarSlideTransform)` and including it in `OverlayManager`'s managed array allows `ToggleSidebarOverlay()` to call `_overlayManager.Show(_sidebarEntry)` or `_overlayManager.CloseAll()`.
   - The customer button (`BtnOrderInfo_Click`) in `ActionBarControl.xaml.cs` already invokes `mw.ToggleSidebarOverlay()`, so wiring remains intact without modifying `ActionBarControl`.

### R1.2: Keeping NavPanel Expanded on Startup
1. *Observation*: In `NavigationService.cs`, `IsExpanded` defaults to `true`.
2. *Observation*: In `MainWindow.xaml`, `NavPanel` is styled with `Width="160"`, `BtnToggleNav` has tooltip `"Свернуть панель навигации"`, and `NavToggleLabel` is visible with text `"Свернуть"`.
3. *Observation*: In `MainWindow.xaml.cs` lines 375–380, `InitializeServices()` explicitly calls `_navService.Collapse(); CalculationView.Margin = new Thickness(52, 0, 0, 0); NavPanel.Width = 52; NavToggleLabel.Visibility = Visibility.Collapsed; BtnToggleNav.ToolTip = "Развернуть панель навигации";`.
4. *Deduction*:
   - The startup collapse is caused solely by lines 375–380 in `MainWindow.xaml.cs`.
   - Removing lines 375–380 allows `MainWindow` to stay in its default XAML state: 160px width, labels visible, `CalculationView.Margin` at `Gap.CalculationView` (`160,0,0,0`).
   - The user can still click `BtnToggleNav` to collapse to 52px or expand to 160px via the existing `BtnToggleNav_Click` handler.

### R2: Segmented Pill Tabs in OrdersHistoryControl
1. *Observation*: `OrdersHistoryControl.xaml` specifies `Style="{DynamicResource SegmentedRadioButton}"`, but `SegmentedRadioButton` was never declared, leading WPF to fall back to the circular radio button style in `InputStyles.RadioButton.xaml`.
2. *Observation*: The requirement mandates: container `SurfaceVariant`, active tab `Accent` background, `Radius.Control` (8px), and `Pad.ButtonSm` (`10,3`), without Windows default radio circles.
3. *Observation*: `SurfaceVariant` is currently missing in `Brushes.xaml` and `ThemeService.cs`, causing `{DynamicResource SurfaceVariant}` to resolve to null/transparent.
4. *Deduction*:
   - Adding `<SolidColorBrush x:Key="SurfaceVariant" Color="#F0F0F4"/>` to `Themes/Brushes.xaml` and updating `ThemeService.cs` (`LightColors`: `#F0F0F4`, `DarkColors`: `#2A2A2E`) provides the valid theme token.
   - Defining `Style x:Key="SegmentedRadioButton" TargetType="RadioButton"` in `Themes/InputStyles.RadioButton.xaml` (or `Themes/TabStyles.xaml` or `OrdersHistoryControl.xaml.Resources`) provides:
     - `Border` with `CornerRadius="{DynamicResource Radius.Control}"` and `Padding="{DynamicResource Pad.ButtonSm}"`.
     - Direct `ContentPresenter` without any radio circle `Border` or bullet `Ellipse`.
     - Trigger on `IsChecked="True"`: `Background="{DynamicResource Accent}"`, `Foreground="{DynamicResource OnAccent}"`.
     - Trigger on hover when inactive: `Background="{DynamicResource AccentLight}"`, `Foreground="{DynamicResource AccentText}"`.
     - Hover when active: `Background="{DynamicResource AccentHover}"`.
   - Container `Border` in `OrdersHistoryControl.xaml` uses:
     `Background="{DynamicResource SurfaceVariant}" CornerRadius="{DynamicResource Radius.Control}" Padding="{DynamicResource Pad.ControlSm}" BorderBrush="{DynamicResource Border}" BorderThickness="1"`
   - Both radio buttons are given `GroupName="OrderHistoryViewMode"`.

---

## 3. Implementation Blueprint

### Step 1: Update `Themes/Brushes.xaml` and `Services/ThemeService.cs` (Token SurfaceVariant)
In `MosquitoNetCalculator/Themes/Brushes.xaml`, add under Color Palette (e.g. after line 8):
```xml
<SolidColorBrush x:Key="SurfaceVariant" Color="#F0F0F4"/>
```
In `MosquitoNetCalculator/Services/ThemeService.cs`:
- In `LightColors` dictionary (around line 292):
  ```csharp
  ["SurfaceVariant"] = "#F0F0F4",
  ```
- In `DarkColors` dictionary (around line 422):
  ```csharp
  ["SurfaceVariant"] = "#2A2A2E",
  ```

### Step 2: Define `SegmentedRadioButton` Style
In `MosquitoNetCalculator/Themes/InputStyles.RadioButton.xaml` (or `MosquitoNetCalculator/Controls/OrdersHistoryControl.xaml.Resources`), add:
```xml
<!-- Segmented pill button style for view toggles -->
<Style x:Key="SegmentedRadioButton" TargetType="RadioButton">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Foreground" Value="{DynamicResource TextSecondary}"/>
    <Setter Property="FontSize" Value="{StaticResource Type.Caption}"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Padding" Value="{DynamicResource Pad.ButtonSm}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="FocusVisualStyle" Value="{StaticResource FluentFocusVisual}"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="RadioButton">
                <Border x:Name="Bd"
                        Background="{TemplateBinding Background}"
                        BorderBrush="Transparent"
                        BorderThickness="0"
                        CornerRadius="{DynamicResource Radius.Control}"
                        Padding="{TemplateBinding Padding}"
                        SnapsToDevicePixels="True">
                    <ContentPresenter HorizontalAlignment="Center"
                                      VerticalAlignment="Center"
                                      RecognizesAccessKey="True"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsChecked" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Accent}"/>
                        <Setter Property="Foreground" Value="{DynamicResource OnAccent}"/>
                    </Trigger>
                    <MultiTrigger>
                        <MultiTrigger.Conditions>
                            <Condition Property="IsMouseOver" Value="True"/>
                            <Condition Property="IsChecked" Value="False"/>
                        </MultiTrigger.Conditions>
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentLight}"/>
                        <Setter Property="Foreground" Value="{DynamicResource AccentText}"/>
                    </MultiTrigger>
                    <MultiTrigger>
                        <MultiTrigger.Conditions>
                            <Condition Property="IsMouseOver" Value="True"/>
                            <Condition Property="IsChecked" Value="True"/>
                        </MultiTrigger.Conditions>
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource AccentHover}"/>
                    </MultiTrigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Opacity" Value="0.5"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

In `MosquitoNetCalculator/Controls/OrdersHistoryControl.xaml`:
Update container border (lines 24–34) to use `Radius.Control`:
```xml
<!-- Segmented View Toggle: [ Все заказы ] [ 🗑 Корзина (N) ] -->
<Border Background="{DynamicResource SurfaceVariant}" CornerRadius="{DynamicResource Radius.Control}"
        Padding="{DynamicResource Pad.ControlSm}" Margin="{DynamicResource Gap.InlineLg}" BorderBrush="{DynamicResource Border}" BorderThickness="1">
    <StackPanel Orientation="Horizontal">
        <RadioButton x:Name="RbActiveOrders" Content="Все заказы"
                     GroupName="OrderHistoryViewMode"
                     IsChecked="True" Checked="RbViewMode_Checked"
                     Style="{DynamicResource SegmentedRadioButton}"/>
        <RadioButton x:Name="RbTrashOrders" Content="🗑 Корзина"
                     GroupName="OrderHistoryViewMode"
                     Checked="RbViewMode_Checked"
                     Style="{DynamicResource SegmentedRadioButton}"/>
    </StackPanel>
</Border>
```

### Step 3: Remove Docked Sidebar from CalculationView in `MainWindow.xaml`
In `MosquitoNetCalculator/MainWindow.xaml`:
1. Remove `<Grid.ColumnDefinitions>` and column 1 `<Border x:Name="SidebarPanel" ...>` from `<Grid x:Name="CalculationView" ...>` (lines 204–301):
```xml
<!-- PRIMARY: РАСЧЁТ view -->
<Grid x:Name="CalculationView" Background="{DynamicResource AppBg}" Margin="{DynamicResource Gap.CalculationView}">
    <Grid Margin="{DynamicResource Gap.Window}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- Action bar -->
            <RowDefinition Height="Auto"/>  <!-- Quick-Add -->
            <RowDefinition Height="*"/>     <!-- Table -->
            <RowDefinition Height="Auto"/>  <!-- ИТОГО -->
            <RowDefinition Height="Auto"/>  <!-- Status bar -->
        </Grid.RowDefinitions>

        <!-- ACTION BAR -->
        <controls:ActionBarControl x:Name="ActionBarControl" Grid.Row="0"/>

        <!-- QUICK-ADD -->
        <controls:QuickAddControl x:Name="QuickAddControl" Grid.Row="1"/>

        <!-- TABLE + EMPTY STATE -->
        <controls:OrderItemsControl x:Name="OrderItemsControl" Grid.Row="2"/>

        <!-- ИТОГО -->
        <controls:TotalCardControl x:Name="TotalCardControl" Grid.Row="3"/>

        <!-- Status bar -->
        <Border Grid.Row="4" Margin="{DynamicResource Gap.BlockTop}" Padding="{DynamicResource Pad.ControlSm}"
                HorizontalAlignment="Stretch" Background="Transparent">
            ...
        </Border>
    </Grid>
</Grid>
```

2. Add `SidebarOverlay` immediately below `CalculationView` (before `OrdersOverlay`):
```xml
<!-- ═══════════════════════════════════════ -->
<!-- OVERLAY: ДАННЫЕ ЗАКАЗА (sidebar)        -->
<!-- ═══════════════════════════════════════ -->
<Grid x:Name="SidebarOverlay" Visibility="Collapsed" Panel.ZIndex="15">
    <Border x:Name="SidebarBackdrop" Background="{DynamicResource Scrim}" Opacity="0"
            MouseLeftButtonDown="Backdrop_MouseLeftButtonDown" Cursor="Hand"/>
    <Border x:Name="SidebarPanel"
            Background="{DynamicResource Surface}"
            HorizontalAlignment="Right" Width="340"
            BorderBrush="{DynamicResource Border}" BorderThickness="1,0,0,0">
        <Border.RenderTransform>
            <TranslateTransform x:Name="SidebarSlideTransform" X="0"/>
        </Border.RenderTransform>
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>
            <Border Grid.Row="0" Style="{StaticResource OverlayHeader}">
                <Grid>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <TextBlock Text="&#xE77B;" FontFamily="{DynamicResource Font.Icon}" FontSize="{StaticResource Type.Subtitle}"
                                   Foreground="{DynamicResource AccentText}" VerticalAlignment="Center" Margin="{DynamicResource Gap.Inline}"/>
                        <TextBlock Text="Данные заказа" FontSize="{StaticResource Type.BodyLg}" FontWeight="SemiBold"
                                   Foreground="{DynamicResource TextPrimary}" VerticalAlignment="Center"/>
                    </StackPanel>
                    <Button Style="{StaticResource OverlayCloseButton}"
                            HorizontalAlignment="Right" Click="CloseSidebar_Click"/>
                </Grid>
            </Border>
            <controls:SidebarControl x:Name="SidebarControl" Grid.Row="1"/>
        </Grid>
    </Border>
</Grid>
```

### Step 4: Wire OverlayManager and NavPanel in `MainWindow.xaml.cs`
In `MosquitoNetCalculator/MainWindow.xaml.cs`:
1. Add field around line 82:
   ```csharp
   private OverlayManager.OverlayEntry? _sidebarEntry;
   ```
2. In `InitializeServices()` (around line 382):
   ```csharp
   _sidebarEntry = new OverlayManager.OverlayEntry(SidebarOverlay, SidebarPanel, SidebarBackdrop, SidebarSlideTransform);
   ```
   Add `_sidebarEntry` to the `OverlayManager` constructor array:
   ```csharp
   _overlayManager = new OverlayManager(
       new[] { _sidebarEntry, _ordersEntry, _pricesEntry, _updatesEntry, _printEntry, _adminEntry, slopeEntry },
       SetActiveNavButton,
       SavePrintSettings);
   ```
3. Remove lines 375–380 (the forced collapse):
   ```csharp
   // DELETE or COMMENT OUT these lines:
   // _navService.Collapse();
   // CalculationView.Margin = new Thickness(52, 0, 0, 0);
   // NavPanel.Width = 52;
   // NavToggleLabel.Visibility = Visibility.Collapsed;
   // BtnToggleNav.ToolTip = "Развернуть панель навигации";
   ```
4. Update `ToggleSidebarOverlay()` and `CloseSidebar_Click` (lines 673–689):
   ```csharp
   internal void ToggleSidebarOverlay()
   {
       if (_sidebarEntry == null) return;
       if (SidebarOverlay.Visibility == Visibility.Visible)
       {
           _overlayManager?.CloseAll();
       }
       else
       {
           _overlayManager?.Show(_sidebarEntry);
           Sidebar.ResetScroll();
       }
   }

   private void CloseSidebar_Click(object sender, RoutedEventArgs e)
   {
       _overlayManager?.CloseAll();
   }
   ```

---

## 4. Caveats
- No code was modified in this investigation (read-only mode strictly respected).
- Any unit tests checking `CalculationView.Margin` on fresh `MainWindow` creation without nav toggle will see `160,0,0,0` instead of `52,0,0,0`. `AiAssistantOverlayLayoutTests.cs` tests `ComputeCalculationViewTargetMargin(true, ...)` which expects `160`, matching this expanded state.
- `SidebarControl` internal bindings (`Sidebar.TxtPrefix`, `Sidebar.CmbOrderStatus`, etc.) remain identical because the element name `SidebarControl` and property `Sidebar` in `MainWindow.xaml.cs` are unchanged.

---

## 5. Conclusion
- **R1**: The right sidebar was previously trapped as a docked column in `CalculationView`. Restoring it as `SidebarOverlay` inside the overlay container with `_sidebarEntry` in `OverlayManager` provides a modal slide-out experience that reclaims full horizontal width for `CalculationView`. The customer button (`BtnOrderInfo_Click`) already invokes `mw.ToggleSidebarOverlay()`.
- **R1 (NavPanel)**: Removing the explicit 5-line startup collapse from `MainWindow.xaml.cs` lines 375–380 keeps `NavPanel` at its default 160px width with icons and labels visible upon launch, while user-driven collapse/expand remains fully functional.
- **R2**: `OrdersHistoryControl` fell back to Windows radio circles because `SegmentedRadioButton` was missing and `SurfaceVariant` was undefined. Declaring `SurfaceVariant` in `Brushes.xaml` / `ThemeService.cs` and adding `SegmentedRadioButton` using `Radius.Control` and `Pad.ButtonSm` cleanly transforms the toggle into modern segmented pill tabs.

---

## 6. Verification Method

### Test Execution
Run the test suite to ensure no regressions in layout, design tokens, and navigation:
```powershell
dotnet test MosquitoNetCalculator.Tests/MosquitoNetCalculator.Tests.csproj --filter "FullyQualifiedName~DesignTokenGuardTests|FullyQualifiedName~NavigationServiceTests|FullyQualifiedName~OrdersHistory|FullyQualifiedName~AiAssistantOverlayLayoutTests" -c Release
```

### Static Inspection Points
1. `MosquitoNetCalculator/MainWindow.xaml`: Verify `SidebarOverlay` exists under overlays and `CalculationView` has no `ColumnDefinitions`.
2. `MosquitoNetCalculator/MainWindow.xaml.cs`: Verify lines 375–380 are removed, `_sidebarEntry` is registered in `_overlayManager`, and `ToggleSidebarOverlay()` invokes `_overlayManager.Show(_sidebarEntry)`.
3. `MosquitoNetCalculator/Controls/OrdersHistoryControl.xaml`: Verify `RbActiveOrders` and `RbTrashOrders` render using `SegmentedRadioButton`, container uses `SurfaceVariant` and `Radius.Control`.
4. `MosquitoNetCalculator/Themes/Brushes.xaml`: Verify `<SolidColorBrush x:Key="SurfaceVariant" .../>` is defined.
5. Invalidation condition: If `dotnet test` fails on `DesignTokenGuardTests` or `NavigationServiceTests`, verify that tokens are referenced via `{DynamicResource}` and `design-token-budget.json` limits are satisfied.
