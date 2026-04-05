namespace SmsGateway.Shared.Models;

public enum ReceiptState
{
    Unknown = 0,
    Delivered = 1,
    Expired = 2,
    Deleted = 3,
    Undeliverable = 4,
    Accepted = 5,
    Rejected = 6
}