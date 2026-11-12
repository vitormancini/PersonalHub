namespace PersonalHub.Web.Domain.Notifications;

/// <summary>Situação de entrega vinda do webhook. A ordem importa: uma atualização nunca volta para trás.</summary>
public enum NotificationDeliveryStatus
{
    Sent = 1,
    Delivered = 2,
    Read = 3,
    Failed = 4
}
