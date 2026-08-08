$ErrorActionPreference = "Stop"
$file = "MosquitoNetCalculator\MainWindow.xaml"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

$aiOverlay = @"
                <!-- ═══════════════════════════════════════ -->
                <!-- OVERLAY: AI ASSISTANT                    -->
                <!-- ═══════════════════════════════════════ -->
                <Grid x:Name="AiOverlay" Visibility="Collapsed" Panel.ZIndex="15">
                    <Border x:Name="AiBackdrop" Background="#80000000" Opacity="0"
                            MouseLeftButtonDown="Backdrop_MouseLeftButtonDown" Cursor="Hand"/>
                    <Border x:Name="AiPanel"
                            Background="{DynamicResource Surface}"
                            HorizontalAlignment="Right" Width="380"
                            BorderBrush="{DynamicResource Border}" BorderThickness="1,0,0,0">
                        <Border.RenderTransform>
                            <TranslateTransform x:Name="AiSlideTransform" X="0"/>
                        </Border.RenderTransform>
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="*"/>
                            </Grid.RowDefinitions>
                            <Border Grid.Row="0" Background="{DynamicResource HeaderBg}"
                                    Padding="16,10" BorderBrush="{DynamicResource Border}" BorderThickness="0,0,0,1">
                                <Grid>
                                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                        <TextBlock Text="&#xE99A;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="16"
                                                   Foreground="{DynamicResource Accent}" VerticalAlignment="Center" Margin="0,0,8,0"/>
                                        <TextBlock Text="AI Ассистент" FontSize="14" FontWeight="SemiBold"
                                                   Foreground="{DynamicResource TextPrimary}" VerticalAlignment="Center"/>
                                    </StackPanel>
                                    <Button Style="{StaticResource OverlayCloseButton}"
                                            HorizontalAlignment="Right" Click="CloseOverlay_Click"/>
                                </Grid>
                            </Border>
                            <controls:AiAssistantControl x:Name="AiAssistantControl" Grid.Row="1"/>
                        </Grid>
                    </Border>
                </Grid>

"@

# Find the sidebar overlay comment and insert AI overlay before it
$marker = "<!-- OVERLAY: ДАННЫЕ ЗАКАЗА (sidebar)"
$idx = $content.IndexOf($marker)
if ($idx -ge 0) {
    # Find the line start (go back to find the newline)
    $lineStart = $idx
    while ($lineStart -gt 0 -and $content[$lineStart - 1] -ne "`n") { $lineStart-- }
    
    $before = $content.Substring(0, $lineStart)
    $after = $content.Substring($lineStart)
    
    $newContent = $before + "`r`n" + $aiOverlay + $after
    [System.IO.File]::WriteAllText($file, $newContent, [System.Text.Encoding]::UTF8)
    Write-Host "SUCCESS: AI overlay inserted before SidebarOverlay"
} else {
    Write-Host "ERROR: Marker not found"
}
