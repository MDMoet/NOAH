using Application.Interfaces;
using Application.Models;
using Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NOAH.Contracts.Assistant;
using NOAH.Contracts.Common;
using NOAH.Contracts.Enums;
using NOAH.Domain.Entities;

namespace Client.UnitTests;

public sealed class AssistantServicePseudoToolRecoveryTests
{
    [Fact]
    public async Task ProcessMessageAsync_RecoversSnakeCaseNearbyToolCallFromFreeFormResponse()
    {
        FakeLlmClient llmClient = new();
        FakeAssistantToolService toolService = new();
        FakeAssistantInteractionRepository interactionRepository = new();
        AssistantService assistantService = new(
            llmClient,
            interactionRepository,
            new FakeAssistantPromptBuilder(),
            toolService,
            new FakeAssistantSettingsService(),
            new FakeAssistantChatService(),
            new FakeAssistantMemoryService(),
            new FakeAssistantModelRouter(),
            new FakeAssistantModelProcessManager(),
            TimeProvider.System,
            NullLogger<AssistantService>.Instance);

        AssistantCommandRequest request = new(
            "Find sushi near me",
            AssistantInputModeDto.Text,
            null,
            new GeoCoordinateDto(51.5542, 5.6821),
            new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero),
            null);

        AssistantCommandResponse response = await assistantService.ProcessMessageAsync(request);

        Assert.Equal(AssistantActionTypeDto.FindNearbyPlaces, response.ActionType);
        Assert.Equal(AssistantInteractionStatusDto.Completed, response.Status);
        Assert.Equal("I found sushi nearby.", response.ResponseText);
        Assert.DoesNotContain("find_nearby_places", response.ResponseText);
        Assert.NotNull(toolService.ExecutedRequest);
        Assert.Equal(AssistantActionTypeDto.FindNearbyPlaces, toolService.ExecutedRequest!.Action.ActionType);
        Assert.Equal("sushi", toolService.ExecutedRequest.Action.Query);
        Assert.NotNull(toolService.ExecutedRequest.Command.CurrentLocation);
        Assert.Equal(51.5542, toolService.ExecutedRequest.Command.CurrentLocation!.Latitude);
        Assert.Equal(5.6821, toolService.ExecutedRequest.Command.CurrentLocation.Longitude);
        Assert.Equal(3, llmClient.GenerateResponseCallCount);
        Assert.NotNull(interactionRepository.UpdatedInteraction);
        Assert.DoesNotContain("find_nearby_places", interactionRepository.UpdatedInteraction!.AssistantResponse);
    }

    private sealed class FakeLlmClient : ILlmClient
    {
        public int GenerateResponseCallCount { get; private set; }

        public Task<LlmChatCompletionResult> GenerateResponseAsync(
            LlmChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            GenerateResponseCallCount++;
            string responseText = request.SystemPrompt.Contains("tool planner", StringComparison.OrdinalIgnoreCase)
                ? "{ \"actionType\": \"Unknown\", \"isPinned\": false, \"createLinkedReminder\": false }"
                : "find_nearby_places(query=\"sushi\", latitude=51.5542, longitude=5.6821";

            return Task.FromResult(new LlmChatCompletionResult(
                request.PrimaryModelKey,
                "test-model",
                responseText,
                "stop",
                false,
                null));
        }

        public Task<IReadOnlyList<LlmModelHealthStatus>> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LlmModelHealthStatus>>([]);
    }

    private sealed class FakeAssistantToolService : IAssistantToolService
    {
        public AssistantPlannedToolActionRequest? ExecutedRequest { get; private set; }

        public Task<AssistantPromptContext> BuildContextAsync(
            AssistantCommandRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssistantPromptContext
            {
                CurrentDateTimeUtc = request.RequestedAtUtc,
                CurrentLocation = request.CurrentLocation
            });

        public Task<AssistantToolActionResult> TryExecuteAsync(
            AssistantToolActionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AssistantToolActionResult.NotHandled);

        public Task<AssistantToolActionResult> ExecutePlannedActionAsync(
            AssistantPlannedToolActionRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecutedRequest = request;
            return Task.FromResult(new AssistantToolActionResult(
                true,
                request.Action.ActionType,
                "I found sushi nearby.",
                null,
                null,
                []));
        }
    }

    private sealed class FakeAssistantInteractionRepository : IAssistantInteractionRepository
    {
        public AssistantInteraction? AddedInteraction { get; private set; }
        public AssistantInteraction? UpdatedInteraction { get; private set; }

        public Task AddAsync(AssistantInteraction assistantInteraction, CancellationToken cancellationToken = default)
        {
            AddedInteraction = assistantInteraction;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AssistantInteraction assistantInteraction, CancellationToken cancellationToken = default)
        {
            UpdatedInteraction = assistantInteraction;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AssistantInteraction>> GetRecentCompletedForScopeAsync(
            Guid? chatId,
            int take,
            Guid? excludeInteractionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AssistantInteraction>>([]);
    }

    private sealed class FakeAssistantPromptBuilder : IAssistantPromptBuilder
    {
        public string BuildPrompt(AssistantCommandRequest request, AssistantPromptContext context) =>
            request.Input;
    }

    private sealed class FakeAssistantSettingsService : IAssistantSettingsService
    {
        private static readonly AssistantSettingsDto Settings = new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            AssistantResponseModeDto.Text,
            "en-US",
            false,
            false,
            false,
            0,
            0);

        public Task<AssistantSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Task<AssistantSettingsDto> UpdateSettingsAsync(
            UpdateAssistantSettingsRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);
    }

    private sealed class FakeAssistantModelRouter : IAssistantModelRouter
    {
        public AssistantModelRoutingDecision Route(AssistantCommandRequest request) =>
            new("test-model", [], "system", "test", false);
    }

    private sealed class FakeAssistantModelProcessManager : IAssistantModelProcessManager
    {
        public void RecordActivity(string modelKey, DateTimeOffset occurredAtUtc)
        {
        }

        public Task EnsureDefaultModelRunningAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EnsureModelReadyAsync(string modelKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public AssistantModelSessionStatus GetSessionStatus(string modelKey, DateTimeOffset currentTimeUtc) =>
            new(modelKey, false, null, "test");

        public Task ReconcileProcessesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAssistantChatService : IAssistantChatService
    {
        public Task<IReadOnlyList<AssistantChatDto>> GetChatsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AssistantChatDto?> GetChatByIdAsync(Guid chatId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(Guid chatId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AssistantChatDto> CreateChatAsync(CreateAssistantChatRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AssistantChatDto?> UpdateChatAsync(
            Guid chatId,
            UpdateAssistantChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteChatAsync(Guid chatId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AssistantInteractionDto>> GetMessagesAsync(
            Guid chatId,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AssistantChatPromptInfo?> GetPromptInfoAsync(Guid chatId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AssistantChatPromptInfo?>(null);

        public Task RecordInteractionAsync(
            Guid chatId,
            string userInput,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAssistantMemoryService : IAssistantMemoryService
    {
        public Task<IReadOnlyList<AssistantMemoryItemDto>> GetMemoryItemsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AssistantMemoryItemDto?> GetMemoryItemByIdAsync(Guid memoryItemId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AssistantMemoryItemDto> CreateMemoryItemAsync(
            CreateAssistantMemoryItemRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AssistantMemoryItemDto?> UpdateMemoryItemAsync(
            Guid memoryItemId,
            UpdateAssistantMemoryItemRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteMemoryItemAsync(Guid memoryItemId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AssistantLongTermMemoryEntry>> GetRelevantMemoryAsync(
            string input,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AssistantLongTermMemoryEntry>>([]);
    }
}