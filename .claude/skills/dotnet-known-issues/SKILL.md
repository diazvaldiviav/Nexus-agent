# Skill: .NET Known Issues — Nexus Agent

> Catalog of known .NET, Avalonia, and SQLite pitfalls that affect the Nexus Agent codebase. Agents MUST consult this before designing, implementing, or reviewing async patterns, database access, and UI code.

---

## Issue 1: Sync Over Async (Deadlock / Thread Pool Starvation)

### Severity: **CRITICAL**

### Error Symptoms
```
Application hangs indefinitely
System.AggregateException: One or more errors occurred.
 ---> System.Threading.Tasks.TaskCanceledException
```

### Root Cause
Calling `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on an async method blocks the calling thread. In UI contexts (Avalonia dispatcher), this causes a deadlock because the continuation needs the same thread that's blocked.

### The Dangerous Pattern (DO NOT USE)
```csharp
// BAD: Blocks thread, causes deadlock in UI context
public string GetData()
{
    var result = _service.GetDataAsync().Result;  // DEADLOCK
    return result;
}

// BAD: Same problem with GetAwaiter
public string GetData()
{
    return _service.GetDataAsync().GetAwaiter().GetResult();  // DEADLOCK
}
```

### The Correct Pattern (ALWAYS USE)
```csharp
// GOOD: Async all the way
public async Task<string> GetDataAsync()
{
    var result = await _service.GetDataAsync();
    return result;
}

// GOOD: In library code, use ConfigureAwait(false)
public async Task<string> GetDataAsync()
{
    var result = await _service.GetDataAsync().ConfigureAwait(false);
    return result;
}
```

---

## Issue 2: HttpClient Socket Exhaustion

### Severity: **HIGH**

### Error Symptoms
```
System.Net.Sockets.SocketException: An attempt was made to access a socket in a way forbidden by its access permissions
System.Net.Http.HttpRequestException: Only one usage of each socket address is normally permitted
```

### Root Cause
Creating a new `HttpClient` per request. `HttpClient` holds socket connections that linger in `TIME_WAIT` state after disposal. Under load, this exhausts available sockets.

### The Dangerous Pattern (DO NOT USE)
```csharp
// BAD: New HttpClient per request
public async Task<string> CallOllamaAsync(string prompt)
{
    using var http = new HttpClient();  // SOCKET EXHAUSTION
    var response = await http.PostAsync(endpoint, content);
    return await response.Content.ReadAsStringAsync();
}
```

### The Correct Pattern (ALWAYS USE)
```csharp
// GOOD: Static HttpClient shared across all calls
public class OllamaLlmProvider : ILlmProvider
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(endpoint, payload, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
```

---

## Issue 3: SQLite Threading / Connection Handling

### Severity: **HIGH**

### Error Symptoms
```
Microsoft.Data.Sqlite.SqliteException: database is locked
System.ObjectDisposedException: Cannot access a disposed object
```

### Root Cause
SQLite is single-writer. Concurrent writes from multiple threads cause "database is locked" errors. Also, sharing a single connection across threads or not disposing connections properly causes `ObjectDisposedException`.

### The Dangerous Pattern (DO NOT USE)
```csharp
// BAD: Shared connection across threads
private readonly SqliteConnection _connection;  // Shared, not thread-safe

public async Task WriteAsync(Entity entity)
{
    // Multiple threads hitting this → "database is locked"
    var cmd = _connection.CreateCommand();
    cmd.CommandText = "INSERT INTO entities ...";
    await cmd.ExecuteNonQueryAsync();
}
```

### The Correct Pattern (ALWAYS USE)
```csharp
// GOOD: Create and dispose connections per operation
public async Task WriteAsync(Entity entity, CancellationToken ct = default)
{
    await using var connection = new SqliteConnection(_connectionString);
    await connection.OpenAsync(ct);

    var cmd = connection.CreateCommand();
    cmd.CommandText = "INSERT INTO entities (name, type) VALUES (@name, @type)";
    cmd.Parameters.AddWithValue("@name", entity.Name);
    cmd.Parameters.AddWithValue("@type", entity.Type);
    await cmd.ExecuteNonQueryAsync(ct);
}

// GOOD: Use WAL mode for better concurrent read performance
private void EnableWalMode(SqliteConnection connection)
{
    var cmd = connection.CreateCommand();
    cmd.CommandText = "PRAGMA journal_mode=WAL;";
    cmd.ExecuteNonQuery();
}
```

---

## Issue 4: Avalonia UI Thread Dispatcher

### Severity: **HIGH**

### Error Symptoms
```
System.InvalidOperationException: Call from invalid thread
Avalonia.Threading.Dispatcher: The calling thread cannot access this object
```

### Root Cause
Modifying UI-bound properties (ObservableProperty, ObservableCollection) from a background thread. Avalonia, like WPF, requires UI changes on the dispatcher thread.

### The Dangerous Pattern (DO NOT USE)
```csharp
// BAD: Updating ObservableCollection from background thread
public async Task LoadEntitiesAsync()
{
    var entities = await _graph.GetAllAsync(); // Runs on thread pool
    Entities.Clear();  // CRASH — wrong thread
    foreach (var e in entities)
        Entities.Add(e);  // CRASH
}
```

### The Correct Pattern (ALWAYS USE)
```csharp
// GOOD: Marshal back to UI thread
public async Task LoadEntitiesAsync()
{
    var entities = await _graph.GetAllAsync();

    await Dispatcher.UIThread.InvokeAsync(() =>
    {
        Entities.Clear();
        foreach (var e in entities)
            Entities.Add(e);
    });
}

// GOOD: Or set ObservableProperty which auto-notifies on any thread
// CommunityToolkit.Mvvm handles thread marshaling for [ObservableProperty]
[ObservableProperty]
private bool _isLoading;  // Safe to set from any thread
```

---

## Issue 5: async void (Fire-and-Forget Exceptions Lost)

### Severity: **MEDIUM**

### Error Symptoms
```
Unhandled exception crashes the application with no clear origin.
Exception is silently swallowed with no logging.
```

### Root Cause
`async void` methods cannot be awaited, so exceptions thrown inside them are unobserved and crash the process (or are silently lost depending on the synchronization context).

### The Dangerous Pattern (DO NOT USE)
```csharp
// BAD: async void — exception crashes the app
private async void LoadData()
{
    var data = await _service.GetDataAsync(); // If this throws, app crashes
    Items = data;
}

// BAD: async void event handler without try/catch
private async void OnButtonClick(object sender, EventArgs e)
{
    await _service.DoWorkAsync(); // Unhandled exception
}
```

### The Correct Pattern (ALWAYS USE)
```csharp
// GOOD: Return Task, use RelayCommand for UI commands
[RelayCommand]
private async Task LoadDataAsync()
{
    try
    {
        var data = await _service.GetDataAsync();
        Items = data;
    }
    catch (Exception ex)
    {
        ErrorMessage = $"Failed to load: {ex.Message}";
    }
}

// ACCEPTABLE: async void event handler WITH try/catch
private async void OnButtonClick(object? sender, EventArgs e)
{
    try
    {
        await _service.DoWorkAsync();
    }
    catch (Exception ex)
    {
        // Handle or log
    }
}
```

---

## Issue 6: Missing CancellationToken Propagation

### Severity: **MEDIUM**

### Root Cause
Not propagating `CancellationToken` through async call chains causes operations to continue even after the user cancels or the app is shutting down. LLM calls can take 10+ seconds — cancellation is essential.

### The Dangerous Pattern (DO NOT USE)
```csharp
// BAD: No cancellation support — LLM call runs forever
public async Task<string> GenerateAsync(string prompt)
{
    var response = await _http.PostAsync(url, content);  // No CT
    return await response.Content.ReadAsStringAsync();    // No CT
}
```

### The Correct Pattern (ALWAYS USE)
```csharp
// GOOD: CancellationToken on all async I/O
public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
{
    var response = await _http.PostAsync(url, content, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync(ct);
}
```

---

## Quick Reference: Pitfall Checklist

| # | Issue | Check |
|---|---|---|
| 1 | Sync over async | No `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` |
| 2 | HttpClient exhaustion | Static `HttpClient` per service, never `new HttpClient()` per call |
| 3 | SQLite threading | Create+dispose connections per operation, use parameterized queries |
| 4 | UI thread dispatch | Modify collections via `Dispatcher.UIThread.InvokeAsync()` |
| 5 | async void | Return `Task`, never `async void` (except event handlers with try/catch) |
| 6 | CancellationToken | Propagate `CancellationToken` through all async chains |
