import os

file_path = os.path.join("MosquitoNetCalculator", "MainWindow.xaml")

with open(file_path, "r", encoding="utf-8-sig") as f:
    content = f.read()

ai_overlay = '''
                <!-- \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550 -->
                <!-- OVERLAY: AI ASSISTANT                    -->
                <!-- \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550 -->
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
                                        <TextBlock Text="AI \u0410\u0441\u0441\u0438\u0441\u0442\u0435\u043d\u0442" FontSize="14" FontWeight="SemiBold"
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

'''

# Find the sidebar overlay section and insert AI overlay before it
marker = '<!-- OVERLAY: \u0414\u0410\u041d\u041d\u042b\u0415 \u0417\u0410\u041a\u0410\u0417\u0410 (sidebar)'
idx = content.find(marker)

if idx >= 0:
    # Find the start of this line (go back to the beginning of the line)
    line_start = idx
    while line_start > 0 and content[line_start - 1] != '\n':
        line_start -= 1
    
    before = content[:line_start]
    after = content[line_start:]
    
    new_content = before + '\n' + ai_overlay + after
    
    with open(file_path, "w", encoding="utf-8-sig") as f:
        f.write(new_content)
    
    print("SUCCESS: AI overlay inserted before SidebarOverlay")
else:
    print("ERROR: Marker not found")
    # Try alternative marker
    marker2 = 'x:Name="SidebarOverlay"'
    idx2 = content.find(marker2)
    if idx2 >= 0:
        print(f"Found alternative marker at position {idx2}")
    else:
        print("Alternative marker not found either")
