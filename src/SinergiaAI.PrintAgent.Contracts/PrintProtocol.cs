namespace SinergiaAI.PrintAgent.Contracts;

public static class PrintProtocol
{
    public const int MaxLongPollSeconds = 20;
    public const string LongPollHeader = "X-PrintAgent-Long-Poll";
}
