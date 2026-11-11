using System.Text;
using PoliClick;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length > 0 && (args.Length != 1 || args[0] != "--demo"))
{
    Console.WriteLine("Запуск демонстрации: dotnet run --project src/PoliClick -- --demo");
    return 1;
}

Console.WriteLine("ИС «ПолиКлик»: запись и прием пациента");
Console.WriteLine("Учебные данные, время в сценарии меняется программно.\n");

var currentTime = DateTime.Today.AddHours(8);
var clinic = new Clinic(() => currentTime);
var doctor = clinic.AddDoctor("Иванова Анна Сергеевна", "Терапия", 20);
var patient = clinic.AddPatient("Петров Алексей Игоревич", new DateOnly(1998, 4, 12), "0000000000000001");
var anotherPatient = clinic.AddPatient("Сидоров Илья Олегович", new DateOnly(1995, 7, 3), "0000000000000002");

var firstDay = currentTime.Date.AddDays(1).AddHours(9);
clinic.CreateSchedule(doctor.Id, "101", firstDay, firstDay.AddHours(1));
clinic.CreateSchedule(doctor.Id, "203", firstDay.AddDays(1), firstDay.AddDays(1).AddHours(1));

Console.WriteLine("1. Свободные слоты на первый день:");
foreach (var slot in clinic.FindFreeSlots("Терапия", DateOnly.FromDateTime(firstDay)))
    Console.WriteLine($"   Слот {slot.Id}: {slot.Start:dd.MM.yyyy HH:mm}–{slot.End:HH:mm}, кабинет {slot.CabinetNumber}");

var firstSlot = clinic.FindFreeSlots("Терапия", DateOnly.FromDateTime(firstDay))[0];
var booking = clinic.BookAppointment(patient.Id, firstSlot.Id);
Console.WriteLine($"\n2. Запись {booking.Number}: {booking.Patient.FullName}, {booking.Doctor.FullName}, кабинет {booking.CabinetNumber}.");

try
{
    clinic.BookAppointment(anotherPatient.Id, firstSlot.Id);
    throw new Exception("Ошибка демонстрации: удалось повторно занять слот.");
}
catch (InvalidOperationException error)
{
    Console.WriteLine($"   Повторная запись отклонена: {error.Message}");
}

// Врач один раз получает объект приема, затем работает с ним локально.
currentTime = firstSlot.Start;
var appointment = clinic.StartAppointment(doctor.Id, booking.Id);
appointment.SetExamination("Учебный пример жалоб", "Учебный анамнез", "Учебное описание осмотра");
appointment.SetDiagnosis("R69");
appointment.AddPrescription("Учебное назначение для демонстрации хранения данных.");
currentTime = currentTime.AddMinutes(17);
appointment.Complete();
Console.WriteLine($"\n3. Прием {appointment.Number} завершен: {appointment.StartedAt:HH:mm}–{appointment.FinishedAt:HH:mm}.");
Console.WriteLine("   Visit и MedicalRecord не создавались: результат находится в Appointment.");

try
{
    appointment.SetDiagnosis("R51");
    throw new Exception("Ошибка демонстрации: завершенный прием удалось изменить.");
}
catch (InvalidOperationException error)
{
    Console.WriteLine($"   Изменение после завершения отклонено: {error.Message}");
}

var secondSlot = clinic.FindFreeSlots("Терапия", DateOnly.FromDateTime(firstDay.AddDays(1)))[0];
var secondAppointment = clinic.BookAppointment(patient.Id, secondSlot.Id);
currentTime = secondSlot.Start;
clinic.StartAppointment(doctor.Id, secondAppointment.Id);
secondAppointment.SetExamination("Жалобы второго приема", "Анамнез второго приема", "Осмотр второго приема");
secondAppointment.SetDiagnosis("R51");
currentTime = currentTime.AddMinutes(15);
secondAppointment.Complete();

Console.WriteLine("\n4. История пациента после двух приемов:");
foreach (var item in patient.GetMedicalHistory())
{
    Console.WriteLine($"   {item.Number}, {item.StartedAt:dd.MM.yyyy HH:mm}, кабинет {item.CabinetNumber}, код {item.DiagnosisCode}");
    Console.WriteLine($"   Жалобы: {item.Complaints}");
}

Console.WriteLine("\nИстория первого приема сохранена. Данные находятся в памяти до завершения программы.");
return 0;
