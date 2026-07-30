using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers
{
    [ApiController]
    [Route("api/sandbox/healthcare")]
    [ApiExplorerSettings(GroupName = "v1-healthcare")]
    public class HealthcareSandboxController : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, object> DataStore = new();

        public record RegisterPatientRequest(string FullName, string DateOfBirth, string InsuranceProvider = "BlueShield", string PolicyNumber = "POL-99201");
        public record HoldSlotRequest(string DoctorId = "DOC-882", string PreferredDate = "2026-07-31", string TimeSlot = "10:00 AM");
        public record ScheduleAppointmentRequest(string PatientId, string SlotHoldId, string Reason = "Annual Telehealth Consultation");
        public record SubmitConsultationNotesRequest(string AppointmentId, string Diagnosis = "Acute Upper Respiratory Infection", string Medication = "Amoxicillin 500mg", int DosageDays = 7);
        public record DispensePrescriptionRequest(string PrescriptionId, string PharmacyBranch = "CVS Pharmacy #4920");

        /// <summary>
        /// Step 1: Register patient demographics & insurance
        /// </summary>
        [HttpPost("patients")]
        public IActionResult RegisterPatient([FromBody] RegisterPatientRequest request)
        {
            var patientId = $"PAT-{Random.Shared.Next(10000, 99999)}";
            var result = new
            {
                patientId,
                fullName = request.FullName ?? "Alex Smith",
                dateOfBirth = request.DateOfBirth ?? "1988-11-23",
                insuranceProvider = request.InsuranceProvider ?? "BlueShield",
                policyNumber = request.PolicyNumber ?? "POL-99201",
                patientStatus = "REGISTERED",
                registeredAt = DateTime.UtcNow
            };
            DataStore[patientId] = result;
            return Created($"/api/sandbox/healthcare/patients/{patientId}", result);
        }

        /// <summary>
        /// Step 2: Hold doctor consultation slot
        /// </summary>
        [HttpPost("appointments/hold-slot")]
        public IActionResult HoldSlot([FromBody] HoldSlotRequest request)
        {
            var slotHoldId = $"SLOT-{Random.Shared.Next(1000, 9999)}";
            var result = new
            {
                slotHoldId,
                doctorId = request.DoctorId ?? "DOC-882",
                doctorName = "Dr. Sarah Connor",
                specialty = "General Practice",
                preferredDate = request.PreferredDate ?? "2026-07-31",
                timeSlot = request.TimeSlot ?? "10:00 AM",
                holdExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            DataStore[slotHoldId] = result;
            return Ok(result);
        }

        /// <summary>
        /// Step 3: Confirm telehealth appointment
        /// </summary>
        [HttpPost("appointments")]
        public IActionResult ScheduleAppointment([FromBody] ScheduleAppointmentRequest request)
        {
            var appointmentId = $"APT-{Random.Shared.Next(10000, 99999)}";
            var result = new
            {
                appointmentId,
                patientId = request.PatientId ?? "PAT-77412",
                slotHoldId = request.SlotHoldId ?? "SLOT-4109",
                reason = request.Reason ?? "Annual Telehealth Consultation",
                appointmentStatus = "SCHEDULED_AND_CONFIRMED",
                scheduledFor = DateTime.UtcNow.AddDays(1)
            };
            DataStore[appointmentId] = result;
            return Created($"/api/sandbox/healthcare/appointments/{appointmentId}", result);
        }

        /// <summary>
        /// Step 4: Doctor submits consultation notes & e-prescription
        /// </summary>
        [HttpPost("consultations/notes")]
        public IActionResult SubmitConsultationNotes([FromBody] SubmitConsultationNotesRequest request)
        {
            var consultationId = $"CNS-{Random.Shared.Next(10000, 99999)}";
            var prescriptionId = $"RX-{Random.Shared.Next(100000, 999999)}";
            var result = new
            {
                consultationId,
                appointmentId = request.AppointmentId ?? "APT-55821",
                prescriptionId,
                diagnosis = request.Diagnosis ?? "Acute Upper Respiratory Infection",
                medication = request.Medication ?? "Amoxicillin 500mg",
                dosageDays = request.DosageDays > 0 ? request.DosageDays : 7,
                instructions = "Take 1 tablet every 8 hours after meals",
                consultedAt = DateTime.UtcNow
            };
            DataStore[prescriptionId] = result;
            return Ok(result);
        }

        /// <summary>
        /// Step 5: Dispense medication & generate pharmacy pickup code
        /// </summary>
        [HttpPost("pharmacy/dispense")]
        public IActionResult DispensePrescription([FromBody] DispensePrescriptionRequest request)
        {
            var fulfillmentId = $"FUL-{Random.Shared.Next(10000, 99999)}";
            var pickupCode = $"PK-{Random.Shared.Next(1000, 9999)}";
            var result = new
            {
                fulfillmentId,
                prescriptionId = request.PrescriptionId ?? "RX-881920",
                pharmacyBranch = request.PharmacyBranch ?? "CVS Pharmacy #4920",
                pickupCode,
                rxStatus = "READY_FOR_PICKUP",
                dispensedAt = DateTime.UtcNow
            };
            DataStore[fulfillmentId] = result;
            return Ok(result);
        }
    }
}
