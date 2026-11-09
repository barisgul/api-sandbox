using AuthSandbox.Infrastructure.Demo;

namespace AuthSandbox.Domain.Banking;

public class BankingState : SandboxState
{
    public Dictionary<string, Customer> Customers { get; } = [];
    public Dictionary<string, KycCase> KycCases { get; } = [];
    public Dictionary<string, Account> Accounts { get; } = [];
    public Dictionary<string, Payment> Payments { get; } = [];
    public Dictionary<string, string> IdempotencyKeys { get; } = [];
    public Dictionary<string, PayeeVerification> PayeeVerifications { get; } = [];
    public Dictionary<string, PaymentRequest> PaymentRequests { get; } = [];
    public Dictionary<string, Consent> Consents { get; } = [];
    public Dictionary<string, InternalTransfer> Transfers { get; } = [];

    /// <summary>Synthetic accounts held at "other banks", used by Verification of Payee.</summary>
    public Dictionary<string, string> ExternalPayees { get; } = [];

    private long _accountNumber;

    public BankingState(TimeProvider clock) : base(clock) => Reset();

    public long NextAccountNumber() => Interlocked.Increment(ref _accountNumber);

    public Account? FindByIban(string iban)
    {
        var n = Validators.NormalizeIban(iban);
        return Accounts.Values.FirstOrDefault(a => a.Iban == n);
    }

    protected override void Clear()
    {
        Customers.Clear(); KycCases.Clear(); Accounts.Clear(); Payments.Clear(); IdempotencyKeys.Clear();
        PayeeVerifications.Clear(); PaymentRequests.Clear(); Consents.Clear(); Transfers.Clear(); ExternalPayees.Clear();
        _accountNumber = 417164301;
    }

    protected override void Seed()
    {
        var now = Now;
        Customer SeedCustomer(string id, string first, string last, string dob, string street, int nr, string pc, string city) =>
            Customers[id] = new Customer
            {
                Id = id, FirstName = first, LastName = last, DateOfBirth = DateOnly.Parse(dob),
                Email = $"{first.ToLowerInvariant()}.{last.Replace(" ", "").ToLowerInvariant()}@example.nl",
                Phone = "+31612345678", Nationality = "NL", KycStatus = KycStatus.APPROVED, CreatedAt = now.AddDays(-90),
                Address = new PostalAddress { Street = street, HouseNumber = nr, Postcode = pc, City = city, Country = "NL" },
            };

        var sanne = SeedCustomer("cus_seed_sanne", "Sanne", "Visser", "1991-04-23", "Keizersgracht", 123, "1015 CJ", "Amsterdam");
        var daan = SeedCustomer("cus_seed_daan", "Daan", "de Boer", "1986-11-02", "Oudegracht", 45, "3511 AB", "Utrecht");

        var sanneAcc = new Account
        {
            Id = "acc_seed_sanne", CustomerId = sanne.Id, Product = AccountProduct.CURRENT, Name = "Everyday account",
            OwnerName = sanne.FullName, Iban = Validators.BuildNlIban("KNAB", 417164301), Bic = "KNABNL2H", OpenedAt = now.AddDays(-89),
        };
        var daanAcc = new Account
        {
            Id = "acc_seed_daan", CustomerId = daan.Id, Product = AccountProduct.BUSINESS, Name = "Business account",
            OwnerName = daan.FullName, Iban = "NL91ABNA0417164300", Bic = "ABNANL2A", OpenedAt = now.AddDays(-89),
        };
        Accounts[sanneAcc.Id] = sanneAcc;
        Accounts[daanAcc.Id] = daanAcc;

        var d = Today;
        Book(sanneAcc, 3150.00m, CreditDebit.CRDT, d.AddDays(-25), "Quixa Demo B.V.", "NL20INGB0001234567", "Salaris oktober", "PMNT-RCDT-ESCT");
        Book(sanneAcc, 1250.00m, CreditDebit.DBIT, d.AddDays(-24), "Woonstichting Grachtengordel", "NL44RABO0123456789", "Huur november", "PMNT-ICDT-ESCT");
        Book(sanneAcc, 64.37m, CreditDebit.DBIT, d.AddDays(-20), "Albert Heijn 1234", null, "Pinbetaling", "PMNT-CCRD-POSD");
        Book(sanneAcc, 18.50m, CreditDebit.CRDT, d.AddDays(-12), "Daan de Boer", daanAcc.Iban, "Tikkie pizza", "PMNT-RCDT-ESCT");
        Book(daanAcc, 5000.00m, CreditDebit.CRDT, d.AddDays(-30), "Klant B.V.", "NL02ABNA0123456789", "Factuur INV-2026-0007", "PMNT-RCDT-ESCT");

        ExternalPayees["NL20INGB0001234567"] = "Quixa Demo B.V.";
        ExternalPayees["NL44RABO0123456789"] = "Woonstichting Grachtengordel";
        ExternalPayees["NL69INGB0123456789"] = "Jan de Vries";
    }

    public BankTransaction Book(Account account, decimal amount, CreditDebit cdi, DateOnly bookingDate, string? counterparty,
        string? counterpartyIban, string? remittance, string code, string endToEndId = "NOTPROVIDED", string? paymentId = null)
    {
        account.Balance = Validators.Money(account.Balance + (cdi == CreditDebit.CRDT ? amount : -amount));
        var tx = new BankTransaction
        {
            Id = NextId("txn"), AccountId = account.Id, BookingDate = bookingDate, ValueDate = bookingDate, Amount = amount,
            Currency = account.Currency, CreditDebitIndicator = cdi, CounterpartyName = counterparty, CounterpartyIban = counterpartyIban,
            RemittanceInformation = remittance, BankTransactionCode = code, EndToEndId = endToEndId, BalanceAfter = account.Balance,
            PaymentId = paymentId,
        };
        account.Transactions.Add(tx);
        return tx;
    }

    public override object Fixtures => new
    {
        customers = Customers.Values.Select(c => new { c.Id, c.FullName, c.KycStatus }),
        accounts = Accounts.Values.Select(a => new { a.Id, a.Product, a.Iban, a.OwnerName, a.Balance }),
        accountProducts = Enum.GetNames<AccountProduct>(),
        externalPayees = ExternalPayees.Select(p => new { iban = p.Key, name = p.Value }),
    };
}
