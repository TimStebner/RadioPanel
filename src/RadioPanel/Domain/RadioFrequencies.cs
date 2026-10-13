namespace RadioPanel.Domain;

public readonly record struct RadioFrequencies(
    int? Com1ActiveHz,
    int? Com1StandbyHz,
    int? Com2ActiveHz,
    int? Com2StandbyHz,
    bool Com1StandbyUnverified = false,
    bool Com2StandbyUnverified = false);
