namespace Api.Helpers;

public sealed class ReminderReferenceNotFoundException(
    string fieldName,
    string errorMessage)
    : Exception(errorMessage)
{
    public string FieldName { get; } = fieldName;

    public string ErrorMessage { get; } = errorMessage;
}
