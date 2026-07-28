
namespace Net7ClientManager.Observations.Models;

using Net7ClientManager.Observations.Observers;

public sealed record ClientCharacterExperienceTrackObservation
{
    public int? Level { get; init; }

    public float? ProgressFraction { get; init; }

    public double? ProgressPercent =>
        this.ProgressFraction.HasValue
            ? this.ProgressFraction.Value * 100.0d
            : null;

    public int? ExperienceRequiredForNextLevel { get; init; }

    public int? ExperienceEarnedInCurrentLevel { get; init; }

    public int? ExperienceRemainingToNextLevel { get; init; }

    public bool IsAtMaximumLevel =>
        this.Level >=
            ClientProgressionRules.MaximumLevel;

    public int? NextLevel =>
        this.Level.HasValue &&
        !this.IsAtMaximumLevel
            ? checked(
                this.Level.Value + 1)
            : null;

    public bool IsComplete =>
        this.Level.HasValue &&
        this.ProgressFraction.HasValue &&
        this.ExperienceRequiredForNextLevel.HasValue &&
        this.ExperienceEarnedInCurrentLevel.HasValue &&
        this.ExperienceRemainingToNextLevel.HasValue;

    public static ClientCharacterExperienceTrackObservation Create(
        int? level,
        float? progressFraction)
    {
        if (!level.HasValue ||
            !progressFraction.HasValue ||
            progressFraction.Value < 0.0f ||
            progressFraction.Value > 1.0f ||
            !ClientProgressionRules.TryGetExperienceToNextLevel(
                level.Value,
                out var experienceRequired))
        {
            return new ClientCharacterExperienceTrackObservation
            {
                Level = level,
                ProgressFraction =
                    progressFraction,
            };
        }

        var experienceEarned = checked(
            (int)Math.Round(
                progressFraction.Value *
                experienceRequired,
                MidpointRounding.AwayFromZero));

        experienceEarned = Math.Clamp(
            experienceEarned,
            0,
            experienceRequired);

        return new ClientCharacterExperienceTrackObservation
        {
            Level = level,
            ProgressFraction =
                progressFraction,
            ExperienceRequiredForNextLevel =
                experienceRequired,
            ExperienceEarnedInCurrentLevel =
                experienceEarned,
            ExperienceRemainingToNextLevel =
                experienceRequired -
                experienceEarned,
        };
    }
}
