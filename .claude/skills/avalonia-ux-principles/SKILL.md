# Skill: Avalonia UX/UI Principles — Nexus Agent Desktop

> Mandatory UX/UI standards for analyzing and improving the Nexus Agent Desktop Avalonia application. Load when analyzing or proposing UI solutions.

---

## 1. Application Views

The Nexus Desktop app has 4 main views:

| View | Purpose | Key UX Requirements |
|---|---|---|
| **ChatView** | Conversation with agent | Input field, message history, processing indicator, model indicator |
| **MemoryGraphView** | Knowledge graph visualization | Colored nodes by type, zoom/pan/click, node detail panel, type filter |
| **SettingsView** | Configuration management | Provider dropdowns, API key field, decay parameters, save feedback |
| **ActionLogView** | Agent action history | Scrollable log, type filter, timestamp/tokens/duration columns |

---

## 2. Visual Hierarchy & Layout

### Spacing and Alignment
- Use consistent padding and margins (multiples of 8: 8, 16, 24, 32)
- Use `Grid`, `StackPanel`, `DockPanel` for structure — avoid absolute positioning
- Group related elements with `Border` or `StackPanel` with consistent spacing

### Typography
- Use clear hierarchy: Headers → Subheaders → Body → Captions
- Ensure sufficient contrast (WCAG AA minimum)
- Use `FontWeight`, `FontSize`, and `Foreground` for hierarchy, not many different fonts

### XAML Layout Patterns
```xml
<!-- GOOD: Consistent spacing with Grid -->
<Grid RowDefinitions="Auto,*,Auto">
    <TextBlock Grid.Row="0" Text="Chat" FontSize="18" FontWeight="Bold" Margin="16" />
    <ScrollViewer Grid.Row="1">
        <ItemsControl ItemsSource="{Binding Messages}" Margin="16,0" />
    </ScrollViewer>
    <DockPanel Grid.Row="2" Margin="16">
        <Button DockPanel.Dock="Right" Command="{Binding SendCommand}" Content="Send" Margin="8,0,0,0" />
        <TextBox Text="{Binding InputText}" />
    </DockPanel>
</Grid>

<!-- BAD: Canvas with absolute positioning -->
<Canvas>
    <TextBox Canvas.Left="10" Canvas.Top="400" Width="300" />
</Canvas>
```

---

## 3. Color Palette for Graph Nodes

| Entity Type | Color | Hex |
|---|---|---|
| Person | Blue | #4A90D9 |
| Project | Green | #27AE60 |
| Technology | Orange | #F39C12 |
| Decision | Red | #E74C3C |
| Date | Gray | #95A5A6 |
| Preference | Purple | #8E44AD |
| Other | White | #FFFFFF |

```xml
<!-- Define as resources in App.axaml -->
<SolidColorBrush x:Key="EntityPersonBrush" Color="#4A90D9" />
<SolidColorBrush x:Key="EntityProjectBrush" Color="#27AE60" />
<SolidColorBrush x:Key="EntityTechnologyBrush" Color="#F39C12" />
<SolidColorBrush x:Key="EntityDecisionBrush" Color="#E74C3C" />
<SolidColorBrush x:Key="EntityDateBrush" Color="#95A5A6" />
<SolidColorBrush x:Key="EntityPreferenceBrush" Color="#8E44AD" />
<SolidColorBrush x:Key="EntityOtherBrush" Color="#FFFFFF" />
```

---

## 4. Interaction & Feedback

### Processing States
- Show a visible indicator when waiting for LLM response (spinner, pulsing dot, "Thinking...")
- Disable input during processing to prevent double-submission
- Show the current model name (local/cloud) alongside the processing indicator

### State Indication
```xml
<!-- GOOD: Processing indicator with model name -->
<StackPanel Orientation="Horizontal" IsVisible="{Binding IsProcessing}">
    <ProgressBar IsIndeterminate="True" Width="100" />
    <TextBlock Text="{Binding CurrentModel}" Margin="8,0" Opacity="0.7" />
</StackPanel>

<!-- GOOD: Disabled input during processing -->
<TextBox Text="{Binding InputText}" IsEnabled="{Binding !IsProcessing}" />
```

### User vs Agent Messages
```xml
<!-- Differentiate visually -->
<DataTemplate x:DataType="vm:ChatMessage">
    <Border Padding="12,8"
            Margin="4"
            CornerRadius="8"
            Background="{Binding Role, Converter={StaticResource RoleToBrushConverter}}"
            HorizontalAlignment="{Binding Role, Converter={StaticResource RoleToAlignConverter}}">
        <TextBlock Text="{Binding Content}" TextWrapping="Wrap" />
    </Border>
</DataTemplate>
```

---

## 5. Memory Graph UX

| Check | Pass | Fail |
|---|---|---|
| Node colors | Match entity type color palette | Wrong or missing colors |
| Node size | Proportional to `relevance_score` | All same size |
| Node interaction | Click shows detail panel | Click does nothing |
| Detail panel | Shows: name, type, summary, score, mentions, last seen | Missing key info |
| Zoom/pan | Mouse wheel zoom, drag to pan | No navigation |
| Filter | Dropdown or toggle to filter by entity type | No filtering |
| Layout | Nodes distributed without overlap | Nodes stacked on top of each other |
| Edge labels | Relation type visible on edges | Unlabeled edges |

---

## 6. Settings UX

| Check | Pass | Fail |
|---|---|---|
| Grouped sections | Model, embeddings, memory in clear sections | Flat list of fields |
| Dropdowns | Provider/model selection via ComboBox | Free text for fixed options |
| API key | PasswordBox style (masked input) | Plaintext API key |
| Save feedback | Visual confirmation on save (SnackBar or checkmark) | No feedback |
| Validation | Invalid values show error border/message | Silent acceptance of bad values |

```xml
<!-- GOOD: Grouped sections with headers -->
<StackPanel Spacing="16" Margin="16">
    <TextBlock Text="LLM Provider" FontWeight="Bold" FontSize="14" />
    <ComboBox ItemsSource="{Binding Providers}" SelectedItem="{Binding SelectedProvider}" />
    <TextBox Watermark="API Key" PasswordChar="•" Text="{Binding ApiKey}" />

    <TextBlock Text="Embeddings" FontWeight="Bold" FontSize="14" Margin="0,16,0,0" />
    <ComboBox ItemsSource="{Binding EmbeddingProviders}" SelectedItem="{Binding SelectedEmbeddingProvider}" />
</StackPanel>
```

---

## 7. Action Log UX

| Check | Pass | Fail |
|---|---|---|
| Table format | Columns: timestamp, type, model, tokens, duration | Missing columns |
| Scrollable | Handles 200+ entries smoothly | Freezes or clips |
| Filter | Filter by action type via ComboBox | No filtering |
| Readable | Formatted timestamps, comma-separated token counts | Raw data |

```xml
<!-- GOOD: DataGrid for action log -->
<DataGrid ItemsSource="{Binding Actions}" IsReadOnly="True" CanUserSortColumns="True">
    <DataGrid.Columns>
        <DataGridTextColumn Header="Time" Binding="{Binding Timestamp, StringFormat='{}{0:HH:mm:ss}'}" />
        <DataGridTextColumn Header="Type" Binding="{Binding ActionType}" />
        <DataGridTextColumn Header="Model" Binding="{Binding ModelUsed}" />
        <DataGridTextColumn Header="Tokens" Binding="{Binding TokenCount, StringFormat='{}{0:N0}'}" />
        <DataGridTextColumn Header="Duration" Binding="{Binding Duration, StringFormat='{}{0:N0}ms'}" />
    </DataGrid.Columns>
</DataGrid>
```

---

## 8. Chat Panel UX

| Check | Pass | Fail |
|---|---|---|
| Input field | Visible, focused by default, Enter to send | Hard to find input |
| Message distinction | User vs agent visually different (alignment, color) | Messages look identical |
| Processing state | Visible indicator when waiting for LLM | No feedback during wait |
| Model indicator | Shows local/cloud model name | No indication of which model |
| Entity feedback | Shows extracted entities after response | No extraction feedback |
| Error display | Helpful error with fix suggestion | Generic error or silent failure |
| Scroll behavior | Auto-scroll to latest message | User must manually scroll |

---

## 9. Accessibility

| Check | Pass | Fail |
|---|---|---|
| Keyboard navigation | Tab order follows visual flow | Can't tab to controls |
| Focus indicators | Visible focus rings on interactive elements | No focus visibility |
| Contrast | Text meets WCAG AA (4.5:1 minimum) | Low contrast text |
| Screen reader | AutomationProperties.Name on key controls | Unnamed controls |

```xml
<!-- GOOD: Accessibility annotations -->
<Button Command="{Binding SendCommand}"
        Content="Send"
        AutomationProperties.Name="Send message"
        AutomationProperties.HelpText="Send the current message to the agent" />
```

---

## Analysis Checklist

When analyzing a UI implementation, check for:
1. [ ] Layout uses Grid/DockPanel/StackPanel — no absolute positioning
2. [ ] Consistent spacing (multiples of 8)
3. [ ] Processing indicator visible during LLM calls
4. [ ] Graph nodes colored per entity type palette
5. [ ] Settings grouped with section headers
6. [ ] API keys masked
7. [ ] Save/action feedback visible
8. [ ] DataGrid used for tabular data (action log)
9. [ ] Keyboard navigation works (Tab order)
10. [ ] Auto-scroll on new messages in chat
