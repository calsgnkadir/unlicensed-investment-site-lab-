namespace Fraud.Domain.OfficialActions;

/// <summary>Bültende yazan faaliyet türü.</summary>
public enum OfficialActionCategory
{
    Crypto = 1,
    Leveraged = 2,
    Other = 3,
}

/// <summary>Resmi kararın geldiği kurum veya yayın.</summary>
public enum OfficialActionSource
{
    SpkBulletin = 1,
}
