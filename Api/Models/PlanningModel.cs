namespace Api.Options;

public sealed class PlanningModel
{
    public const string SectionName = "Planning";

    public string DefaultTimeZoneId { get; set; } = "Europe/Amsterdam";

    public int MaxUpcomingDays { get; set; } = 31;
}
