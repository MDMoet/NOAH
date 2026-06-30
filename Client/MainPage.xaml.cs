using System.Collections.Specialized;
using System.ComponentModel;
using Client.Models;
using Client.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace Client;

public partial class MainPage : ContentPage
{
    private const int MessageScrollDelayMilliseconds = 90;
    private static readonly int[] HistoryScrollSettleDelays = [80, 220, 520, 1000, 1800];

    private readonly MainPageViewModel viewModel;
    private readonly HashSet<ChatMessageItem> observedMessages = [];
    private int pendingMessageScrollRequest;
    private bool isInitialized;
    private bool isDrawerPanTriggered;

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DesktopMessagesCollectionView.HandlerChanged += OnMessagesCollectionViewHandlerChanged;
        MobileMessagesCollectionView.HandlerChanged += OnMessagesCollectionViewHandlerChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        viewModel.PrepareForNavigation();

        if (isInitialized)
        {
            QueueHistoryScrollToBottom();
            return;
        }

        isInitialized = true;
        await viewModel.InitializeAsync();
        TrackCurrentMessages();
        QueueHistoryScrollToBottom();
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            UntrackAllMessages();
            TrackCurrentMessages();
        }
        else
        {
            UntrackMessages(e.OldItems);
            TrackMessages(e.NewItems);
        }

        if (viewModel.Messages.Count == 0 || viewModel.IsLoadingMessages)
        {
            return;
        }

        QueueLiveScrollToBottom(animate: e.Action == NotifyCollectionChangedAction.Add);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainPageViewModel.IsLoadingMessages) &&
            !viewModel.IsLoadingMessages &&
            viewModel.Messages.Count > 0)
        {
            QueueHistoryScrollToBottom();
        }
    }

    private void OnMessagesCollectionViewHandlerChanged(object? sender, EventArgs e)
    {
        QueueHistoryScrollToBottom();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ChatMessageItem message ||
            viewModel.Messages.Count == 0 ||
            !ReferenceEquals(message, viewModel.Messages[^1]))
        {
            return;
        }

        if (e.PropertyName is nameof(ChatMessageItem.Text) or nameof(ChatMessageItem.MetaText) or nameof(ChatMessageItem.IsPending))
        {
            QueueLiveScrollToBottom(animate: true);
        }
    }

    private void TrackCurrentMessages()
    {
        foreach (ChatMessageItem message in viewModel.Messages)
        {
            TrackMessage(message);
        }
    }

    private void TrackMessages(System.Collections.IList? messages)
    {
        if (messages == null)
        {
            return;
        }

        foreach (object item in messages)
        {
            if (item is ChatMessageItem message)
            {
                TrackMessage(message);
            }
        }
    }

    private void TrackMessage(ChatMessageItem message)
    {
        if (observedMessages.Add(message))
        {
            message.PropertyChanged += OnMessagePropertyChanged;
        }
    }

    private void UntrackMessages(System.Collections.IList? messages)
    {
        if (messages == null)
        {
            return;
        }

        foreach (object item in messages)
        {
            if (item is ChatMessageItem message)
            {
                UntrackMessage(message);
            }
        }
    }

    private void UntrackMessage(ChatMessageItem message)
    {
        if (observedMessages.Remove(message))
        {
            message.PropertyChanged -= OnMessagePropertyChanged;
        }
    }

    private void UntrackAllMessages()
    {
        foreach (ChatMessageItem message in observedMessages)
        {
            message.PropertyChanged -= OnMessagePropertyChanged;
        }

        observedMessages.Clear();
    }

    private void QueueLiveScrollToBottom(bool animate)
    {
        int requestId = Interlocked.Increment(ref pendingMessageScrollRequest);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(MessageScrollDelayMilliseconds);

            if (requestId != pendingMessageScrollRequest)
            {
                return;
            }

            ScrollMessagesToBottom(animate, useItemFallback: false);
        });
    }

    private void QueueHistoryScrollToBottom()
    {
        int requestId = Interlocked.Increment(ref pendingMessageScrollRequest);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            foreach (int delayMilliseconds in HistoryScrollSettleDelays)
            {
                await Task.Delay(delayMilliseconds);

                if (requestId != pendingMessageScrollRequest)
                {
                    return;
                }

                ScrollMessagesToBottom(animate: false, useItemFallback: true);
            }
        });
    }

    private void ScrollMessagesToBottom(bool animate, bool useItemFallback)
    {
        int lastMessageIndex = viewModel.Messages.Count - 1;

        if (lastMessageIndex < 0)
        {
            return;
        }

        ChatMessageItem lastMessage = viewModel.Messages[lastMessageIndex];
        ScrollCollectionToMessage(DesktopMessagesCollectionView, lastMessageIndex, lastMessage, animate, useItemFallback);
        ScrollCollectionToMessage(MobileMessagesCollectionView, lastMessageIndex, lastMessage, animate, useItemFallback);
    }

    private static void ScrollCollectionToMessage(
        CollectionView collectionView,
        int itemIndex,
        object item,
        bool animate,
        bool useItemFallback)
    {
        try
        {
            collectionView.ScrollTo(itemIndex, -1, ScrollToPosition.End, animate);

            if (useItemFallback)
            {
                collectionView.ScrollTo(item, position: ScrollToPosition.End, animate: false);
            }
        }
        catch
        {
            // CollectionView can reject ScrollTo before its handler/layout is ready; the next chat event will retry.
        }
    }

    private void OnMobileDrawerPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                isDrawerPanTriggered = false;
                break;

            case GestureStatus.Running:
                if (!isDrawerPanTriggered && e.TotalX >= 56)
                {
                    isDrawerPanTriggered = true;
                    viewModel.OpenDrawer();
                }

                break;

            case GestureStatus.Canceled:
            case GestureStatus.Completed:
                isDrawerPanTriggered = false;
                break;
        }
    }
}