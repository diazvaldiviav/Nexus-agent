using Nexus.Core.Abstractions;
using Nexus.Desktop.Tests.Fakes;
using Nexus.Desktop.ViewModels;

namespace Nexus.Desktop.Tests;

public class ChatViewModelTests
{
    private static FakeAgentService CreateFakeService() => new();

    private static TestableChatViewModel CreateTestableViewModel(FakeAgentService? fake = null)
        => new(fake ?? CreateFakeService());

    private static ChatViewModel CreateViewModel()
        => new(CreateFakeService());

    [Fact]
    public void CanSend_ReturnsFalse_WhenInputTextEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act & Assert
        Assert.False(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void CanSend_ReturnsFalse_WhenIsProcessing()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.InputText = "hello";
        vm.IsProcessing = true;

        // Act & Assert
        Assert.False(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void CanSend_ReturnsTrue_WhenInputTextSet()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.InputText = "hello";

        // Act & Assert
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Messages_InitiallyEmpty()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        Assert.Empty(vm.Messages);
    }

    [Fact]
    public void HasMessages_WhenEmpty_ReturnsFalse()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        Assert.False(vm.HasMessages);
    }

    [Fact]
    public void HasMessages_AfterAdd_ReturnsTrue()
    {
        // Arrange
        var vm = CreateViewModel();
        var propertyChangedNames = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                propertyChangedNames.Add(args.PropertyName);
        };

        // Act
        vm.Messages.Add(new ChatMessage { Role = "user", Content = "hello" });

        // Assert
        Assert.True(vm.HasMessages);
        Assert.Contains(nameof(ChatViewModel.HasMessages), propertyChangedNames);
    }

    [Fact]
    public async Task SendAsync_WhenServiceThrows_SetsHasErrorTrue()
    {
        // Arrange
        var fake = CreateFakeService();
        fake.ExceptionToThrow = new HttpRequestException("Connection refused");
        var vm = CreateTestableViewModel(fake);
        vm.InputText = "hello";

        // Act
        await vm.SendCommand.ExecuteAsync(null);

        // Assert
        Assert.True(vm.HasError);
        Assert.Contains("connect", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Connection refused", vm.ErrorDetail);
    }

    [Fact]
    public async Task RetryAsync_ResendLastMessage()
    {
        // Arrange
        var fake = CreateFakeService();
        fake.ExceptionToThrow = new HttpRequestException("Connection refused");
        var vm = CreateTestableViewModel(fake);
        vm.InputText = "hello";
        await vm.SendCommand.ExecuteAsync(null);

        // Fix the service so retry succeeds
        fake.ExceptionToThrow = null;
        fake.ReceivedMessages.Clear();

        // Act
        await vm.RetryCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("hello", fake.ReceivedMessages);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void DismissError_ClearsErrorState()
    {
        // Arrange
        var vm = CreateTestableViewModel();
        vm.HasError = true;
        vm.ErrorMessage = "some error";
        vm.ErrorDetail = "some detail";

        // Act
        vm.DismissErrorCommand.Execute(null);

        // Assert
        Assert.False(vm.HasError);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("", vm.ErrorDetail);
    }

    [Fact]
    public async Task SendAsync_NewMessage_ClearsExistingError()
    {
        // Arrange
        var fake = CreateFakeService();
        fake.ExceptionToThrow = new HttpRequestException("fail");
        var vm = CreateTestableViewModel(fake);
        vm.InputText = "first";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);

        // Fix the service
        fake.ExceptionToThrow = null;
        vm.InputText = "second";

        // Act
        await vm.SendCommand.ExecuteAsync(null);

        // Assert
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task SendAsync_WhenServiceThrows_AssistantMessageHasIsError()
    {
        // Arrange
        var fake = CreateFakeService();
        fake.ExceptionToThrow = new TaskCanceledException("Timed out");
        var vm = CreateTestableViewModel(fake);
        vm.InputText = "hello";

        // Act
        await vm.SendCommand.ExecuteAsync(null);

        // Assert
        var errorMsg = vm.Messages.LastOrDefault(m => m.Role == "assistant");
        Assert.NotNull(errorMsg);
        Assert.True(errorMsg.IsError);
        Assert.Equal("error", errorMsg.ModelInfo);
    }

    [Fact]
    public async Task ClearHistoryAsync_ClearsMessages_SetsStatusText()
    {
        // Arrange
        var vm = CreateTestableViewModel();
        vm.Messages.Add(new ChatMessage { Role = "user", Content = "hello" });
        vm.Messages.Add(new ChatMessage { Role = "assistant", Content = "world" });
        Assert.True(vm.HasMessages);

        // Act
        await vm.ClearHistoryCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(vm.Messages);
        Assert.False(vm.HasMessages);
        Assert.Equal("History cleared", vm.StatusText);
    }

    [Fact]
    public void SetExamplePrompt_PopulatesInputText()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.SetExamplePromptCommand.Execute("test");

        // Assert
        Assert.Equal("test", vm.InputText);
    }

    [Fact]
    public async Task SendAsync_ToolToken_RoutedToStatusText()
    {
        // Arrange
        var fake = CreateFakeService();
        fake.TokensToYield = ["[Executing tool: search]", "result text"];
        var vm = CreateTestableViewModel(fake);
        vm.InputText = "search for something";

        // Track all StatusText values during execution
        var statusHistory = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChatViewModel.StatusText))
                statusHistory.Add(vm.StatusText);
        };

        // Act
        await vm.SendCommand.ExecuteAsync(null);

        // Assert — tool token routed to StatusText, not to assistant content
        var assistantMsg = vm.Messages.Last(m => m.Role == "assistant");
        Assert.DoesNotContain("[Executing tool:", assistantMsg.Content);
        Assert.Equal("result text", assistantMsg.Content);
        Assert.Contains(statusHistory, s => s.Contains("search", StringComparison.OrdinalIgnoreCase));
    }

    private class TestableChatViewModel : ChatViewModel
    {
        public TestableChatViewModel(IAgentService agentService) : base(agentService) { }
        protected override void DispatchToUI(Action action) => action();
    }
}
