namespace PoliClick;

internal static class QueueDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("ИС 'ПолиКлик': автоматическая очередь");
        Console.WriteLine("Одна секунда демонстрации соответствует пяти минутам расписания.");
        Console.WriteLine("Начало, завершение приемов и отметка неявки заданы сценарием.\n");

        var currentTime = DateTime.Today.AddHours(8);
        var clinic = new Clinic(() => currentTime);
        var therapist = clinic.AddDoctor("Иванова Анна", "Терапия", 20);
        var surgeon = clinic.AddDoctor("Петров Олег", "Хирургия", 20);
        var ophthalmologist = clinic.AddDoctor("Смирнова Елена", "Офтальмология", 20);
        var day = currentTime.Date.AddDays(1).AddHours(9);
        var therapy = clinic.CreateSchedule(therapist.Id, "101", day, day.AddHours(1));
        var surgery = clinic.CreateSchedule(surgeon.Id, "102", day, day.AddHours(1));
        var ophthalmology = clinic.CreateSchedule(ophthalmologist.Id, "201", day, day.AddHours(1));
        clinic.AddQueueBoard("Холл 1", "101", "102", "103");
        clinic.AddQueueBoard("Холл 2", "201", "202", "203");

        var first = Book("Первый пациент", therapy[0]);
        var second = Book("Второй пациент", therapy[1]);
        var absent = Book("Отсутствующий пациент", surgery[0]);
        var fourth = Book("Четвертый пациент", surgery[1]);
        var fifth = Book("Пятый пациент", ophthalmology[0]);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        for (var step = 0; step <= 8; step++)
        {
            if (step > 0)
                await timer.WaitForNextTickAsync();
            currentTime = day.AddMinutes(-5 + step * 5);
            Console.WriteLine($"{currentTime:HH:mm}");

            switch (step)
            {
                case 2: // 09:05
                    clinic.StartAppointment(therapist.Id, first.Id);
                    clinic.StartAppointment(ophthalmologist.Id, fifth.Id);
                    Console.WriteLine("  Начались приемы в кабинетах 101 и 201. Пациент кабинета 102 не подошел.");
                    break;
                case 4: // 09:15
                    clinic.MarkNoShow(surgeon.Id, absent.Id);
                    Console.WriteLine($"  Врач отметил неявку по записи {absent.Number}. Следующее время еще не наступило.");
                    break;
                case 5: // 09:20
                    Console.WriteLine("  Прием в кабинете 101 затянулся: следующего пациента пока не вызываем.");
                    break;
                case 6: // 09:25
                    Complete(first);
                    Complete(fifth);
                    clinic.StartAppointment(surgeon.Id, fourth.Id);
                    Console.WriteLine("  Приемы в кабинетах 101 и 201 завершены. В кабинете 102 начался следующий прием.");
                    break;
                case 7: // 09:30
                    clinic.StartAppointment(therapist.Id, second.Id);
                    Complete(fourth);
                    Console.WriteLine("  В кабинете 101 начался второй прием, в кабинете 102 прием завершен.");
                    break;
                case 8: // 09:35
                    Complete(second);
                    Console.WriteLine("  Второй прием в кабинете 101 завершен.");
                    break;
            }

            // очередь обновляет программа по таймеру, отдельного действия врача для вызова нет
            var called = clinic.UpdateQueue();
            foreach (var appointment in called)
                Console.WriteLine($"  [сигнал] Запись {appointment.Number}, кабинет {appointment.CabinetNumber}");

            foreach (var board in clinic.QueueBoards)
            {
                var calls = board.Calls.OrderBy(c => c.Key)
                    .Select(c => $"{c.Value} → кабинет {c.Key}");
                Console.WriteLine($"  Табло «{board.Id}»: {(board.Calls.Count == 0 ? "нет вызовов" : string.Join("; ", calls))}");
            }
            Console.WriteLine();
        }

        Appointment Book(string name, TimeSlot slot)
        {
            var number = (clinic.Patients.Count + 1).ToString("D16");
            var patient = clinic.AddPatient(name, new DateOnly(1990, 1, 1), number);
            return clinic.BookAppointment(patient.Id, slot.Id);
        }

        static void Complete(Appointment appointment)
        {
            appointment.SetExamination("Учебные жалобы", "Учебный анамнез", "Учебный осмотр");
            appointment.SetDiagnosis("R69");
            appointment.Complete();
        }
    }
}
