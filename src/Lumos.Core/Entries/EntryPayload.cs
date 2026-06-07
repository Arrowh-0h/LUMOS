using System.Text.Json.Serialization;

namespace Lumos.Core.Entries;

/// <summary>
/// Base discriminator for payload types. The JSON polymorphic attributes let
/// us serialize/deserialize the correct concrete type based on a discriminator
/// field, without ever writing manual switch statements.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LoginPayload), typeDiscriminator: "login")]
[JsonDerivedType(typeof(SecureNotePayload), typeDiscriminator: "note")]
[JsonDerivedType(typeof(CardPayload), typeDiscriminator: "card")]
[JsonDerivedType(typeof(IdentityPayload), typeDiscriminator: "identity")]
public abstract record EntryPayload
{
    public abstract EntryType Type { get; }
}

/// <summary>Login entry — username/password plus an optional TOTP secret.</summary>
public sealed record LoginPayload(
    string Username = "",
    string Password = "",
    string Url = "",
    string? TotpSecret = null) : EntryPayload
{
    public override EntryType Type => EntryType.Login;
}

/// <summary>Free-form encrypted note.</summary>
public sealed record SecureNotePayload(
    string Body = "") : EntryPayload
{
    public override EntryType Type => EntryType.SecureNote;
}

/// <summary>Payment card.</summary>
public sealed record CardPayload(
    string CardholderName = "",
    string Number = "",
    string ExpiryMonth = "",
    string ExpiryYear = "",
    string Cvv = "",
    string? Brand = null) : EntryPayload
{
    public override EntryType Type => EntryType.Card;
}

/// <summary>Identity record (name, address, etc).</summary>
public sealed record IdentityPayload(
    string FullName = "",
    string Email = "",
    string Phone = "",
    string Address = "",
    string City = "",
    string Region = "",
    string PostalCode = "",
    string Country = "",
    string? NationalId = null) : EntryPayload
{
    public override EntryType Type => EntryType.Identity;
}
