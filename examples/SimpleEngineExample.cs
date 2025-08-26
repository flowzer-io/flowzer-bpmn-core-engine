using core_engine;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Examples
{
    /// <summary>
    /// Beispiel für die Verwendung der BPMN Core Engine
    /// </summary>
    public class SimpleEngineExample
    {
        public async Task RunSimpleApprovalProcess()
        {
            // 1. Engine initialisieren
            var engine = new CoreEngine(); // Diese Klasse muss noch implementiert werden
            
            // 2. BPMN-Prozess laden
            var bpmnFilePath = "examples/simple-approval-process.bpmn";
            using var xmlStream = File.OpenRead(bpmnFilePath);
            
            await engine.LoadBpmnFile(xmlStream, verify: true);
            Console.WriteLine("BPMN-Prozess erfolgreich geladen");
            
            // 3. Anfangs-Subscriptions abrufen
            var subscriptions = await engine.GetInitialSubscriptions();
            Console.WriteLine($"Gefunden: {subscriptions.Length} Start-Subscriptions");
            
            foreach (var subscription in subscriptions)
            {
                Console.WriteLine($"  - Service: {subscription.ServiceId}, Node: {subscription.BpmnNodeId}");
            }
            
            // 4. Neue Prozessinstanz starten
            var instanceId = Guid.NewGuid();
            var startEventData = new EventData
            {
                BpmnNodeId = "StartEvent_1",
                InstanceId = instanceId
            };
            
            var instanceData = new Instance(); // Diese Klasse muss erweitert werden
            
            // 5. Start-Event verarbeiten
            var result = await engine.HandleEvent(instanceData, startEventData);
            Console.WriteLine($"Prozess gestartet. Nächste Interaktionen: {result.Interactions?.Count ?? 0}");
            
            // 6. User Task verarbeiten (simuliert)
            if (result.Interactions?.Count > 0)
            {
                var userTask = result.Interactions[0];
                if (userTask is UserTask task)
                {
                    Console.WriteLine($"User Task erhalten: {task.Name}");
                    
                    // Simulation: Benutzer genehmigt das Dokument
                    var userTaskCompletionData = new EventData
                    {
                        BpmnNodeId = task.NodeId,
                        InstanceId = instanceId
                    };
                    
                    // Zusätzliche Daten für Entscheidung hinzufügen
                    userTaskCompletionData.AdditionalData = new Dictionary<string, object>
                    {
                        ["approval"] = "approved"
                    };
                    
                    var completionResult = await engine.HandleEvent(instanceData, userTaskCompletionData);
                    Console.WriteLine($"User Task abgeschlossen. Nächste Interaktionen: {completionResult.Interactions?.Count ?? 0}");
                }
            }
        }
        
        /// <summary>
        /// Beispiel für Event-Handler Registration
        /// </summary>
        public void SetupEventHandlers()
        {
            var engine = new CoreEngine();
            
            // Event-Handler für abgeschlossene Interaktionen
            engine.InteractionFinished += (sender, instance) =>
            {
                Console.WriteLine($"Interaction beendet für Instanz: {instance.Id}");
                // Hier könnte externe Verarbeitung stattfinden
                // z.B. Benachrichtigungen senden, Daten speichern, etc.
            };
        }
        
        /// <summary>
        /// Beispiel für Service-Integration
        /// </summary>
        public async Task HandleServiceTask(ServiceTask serviceTask, Dictionary<string, object> processData)
        {
            switch (serviceTask.NodeId)
            {
                case "ServiceTask_1": // Approval Notification
                    await SendApprovalNotification(processData);
                    break;
                    
                case "ServiceTask_2": // Rejection Notification
                    await SendRejectionNotification(processData);
                    break;
                    
                default:
                    Console.WriteLine($"Unbekannte Service Task: {serviceTask.NodeId}");
                    break;
            }
        }
        
        private async Task SendApprovalNotification(Dictionary<string, object> processData)
        {
            // Simulation einer Email-Benachrichtigung
            Console.WriteLine("✅ Genehmigungsbenachrichtigung gesendet");
            await Task.Delay(100); // Simuliert asynchrone Operation
        }
        
        private async Task SendRejectionNotification(Dictionary<string, object> processData)
        {
            // Simulation einer Email-Benachrichtigung
            Console.WriteLine("❌ Ablehnungsbenachrichtigung gesendet");
            await Task.Delay(100); // Simuliert asynchrone Operation
        }
    }
    
    /// <summary>
    /// Hauptprogramm zum Ausführen der Beispiele
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 Flowzer BPMN Core Engine - Beispiel");
            Console.WriteLine("=====================================");
            
            try
            {
                var example = new SimpleEngineExample();
                
                // Event-Handler einrichten
                example.SetupEventHandlers();
                
                // Einfachen Approval-Prozess ausführen
                await example.RunSimpleApprovalProcess();
                
                Console.WriteLine("\n✅ Beispiel erfolgreich abgeschlossen!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Ausführen des Beispiels: {ex.Message}");
                Console.WriteLine($"Details: {ex}");
            }
            
            Console.WriteLine("\nDrücken Sie eine beliebige Taste zum Beenden...");
            Console.ReadKey();
        }
    }
}