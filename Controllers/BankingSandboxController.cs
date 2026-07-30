using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers
{
    [ApiController]
    [Route("api/sandbox/banking")]
    [ApiExplorerSettings(GroupName = "v1-banking")]
    public class BankingSandboxController : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, object> DataStore = new();

        public record CreateCustomerRequest(string FirstName, string LastName, string Email, string DateOfBirth);
        public record SubmitKycRequest(string DocumentType, string DocumentNumber, string Country);
        public record CreateCheckingAccountRequest(string CustomerId, string Currency = "EUR");
        public record CreateSavingsAccountRequest(string CustomerId, string PrimaryAccountId, string AccountName = "High-Yield Savings");
        public record DepositRequest(string AccountId, decimal Amount, string SourceDescription = "Initial Funding Transfer");

        /// <summary>
        /// Step 1: Register customer profile
        /// </summary>
        [HttpPost("customers")]
        public IActionResult CreateCustomer([FromBody] CreateCustomerRequest request)
        {
            var customerId = $"CUST-{Random.Shared.Next(10000, 99999)}";
            var result = new
            {
                customerId,
                firstName = request.FirstName ?? "Maria",
                lastName = request.LastName ?? "Garcia",
                email = request.Email ?? "m.garcia@example.com",
                dateOfBirth = request.DateOfBirth ?? "1990-05-14",
                kycStatus = "PENDING",
                createdAt = DateTime.UtcNow
            };
            DataStore[customerId] = result;
            return Created($"/api/sandbox/banking/customers/{customerId}", result);
        }

        /// <summary>
        /// Step 2: Submit identity check & approval for customer
        /// </summary>
        [HttpPost("customers/{customerId}/kyc")]
        public IActionResult SubmitKyc([FromRoute] string customerId, [FromBody] SubmitKycRequest request)
        {
            var kycId = $"KYC-{Random.Shared.Next(1000, 9999)}";
            var result = new
            {
                kycId,
                customerId,
                documentType = request.DocumentType ?? "Passport",
                documentNumber = request.DocumentNumber ?? "N982144",
                country = request.Country ?? "NL",
                kycStatus = "APPROVED",
                riskScore = "LOW",
                verifiedAt = DateTime.UtcNow
            };
            DataStore[kycId] = result;
            return Ok(result);
        }

        /// <summary>
        /// Step 3: Open primary checking account
        /// </summary>
        [HttpPost("accounts/checking")]
        public IActionResult CreateCheckingAccount([FromBody] CreateCheckingAccountRequest request)
        {
            var accountId = $"ACC-{Random.Shared.Next(100000, 999999)}";
            var iban = $"NL91QUIX0{Random.Shared.Next(100000000, 999999999)}";
            var result = new
            {
                accountId,
                customerId = request.CustomerId ?? "CUST-33912",
                accountType = "Checking",
                iban,
                currency = request.Currency ?? "EUR",
                balance = 0.00m,
                status = "ACTIVE",
                createdAt = DateTime.UtcNow
            };
            DataStore[accountId] = result;
            return Created($"/api/sandbox/banking/accounts/{accountId}", result);
        }

        /// <summary>
        /// Step 4: Open savings sub-account linked to checking
        /// </summary>
        [HttpPost("accounts/savings")]
        public IActionResult CreateSavingsAccount([FromBody] CreateSavingsAccountRequest request)
        {
            var savingsAccountId = $"SAV-{Random.Shared.Next(100000, 999999)}";
            var savingsIban = $"NL91QUIX0{Random.Shared.Next(100000000, 999999999)}";
            var result = new
            {
                savingsAccountId,
                customerId = request.CustomerId ?? "CUST-33912",
                primaryAccountId = request.PrimaryAccountId ?? "ACC-100293",
                accountName = request.AccountName ?? "High-Yield Savings",
                iban = savingsIban,
                interestRatePercentage = 3.75,
                balance = 0.00m,
                status = "ACTIVE",
                createdAt = DateTime.UtcNow
            };
            DataStore[savingsAccountId] = result;
            return Created($"/api/sandbox/banking/accounts/{savingsAccountId}", result);
        }

        /// <summary>
        /// Step 5: Perform deposit transaction to mature account balance
        /// </summary>
        [HttpPost("accounts/deposit")]
        public IActionResult DepositFunds([FromBody] DepositRequest request)
        {
            var transactionId = $"TXN-{Random.Shared.Next(1000000, 9999999)}";
            var result = new
            {
                transactionId,
                accountId = request.AccountId ?? "ACC-100293",
                depositedAmount = request.Amount > 0 ? request.Amount : 10000.00m,
                newBalance = request.Amount > 0 ? request.Amount : 10000.00m,
                currency = "EUR",
                sourceDescription = request.SourceDescription ?? "Initial Funding Transfer",
                accountMaturityStatus = "MATURED_AND_FULLY_FUNDED",
                completedAt = DateTime.UtcNow
            };
            DataStore[transactionId] = result;
            return Ok(result);
        }
    }
}
