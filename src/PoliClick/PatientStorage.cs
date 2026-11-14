namespace PoliClick;

public sealed class PatientStorage
{
    private readonly List<Patient> patients = [];

    public IReadOnlyList<Patient> Patients => patients.AsReadOnly();

    internal Patient AddPatient(string fullName, DateOnly birthDate, string policyNumber, bool isAttached)
    {
        if (patients.Any(p => p.PolicyNumber == policyNumber))
            throw new InvalidOperationException("Пациент с таким полисом уже есть.");

        var patient = new Patient(patients.Count + 1, fullName, birthDate, policyNumber, isAttached);
        patients.Add(patient);
        return patient;
    }

    public Patient GetPatient(int patientId) => patients.FirstOrDefault(p => p.Id == patientId)
        ?? throw new KeyNotFoundException("Пациент не найден.");
}
