namespace Client.Models;

public class ChatMessage
{
    public enum Sender
    {
        User,
        AI
    }

    public Sender From { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }

    public bool IsFromUser => From == Sender.User;
    public bool IsFromAI => From == Sender.AI;
}
