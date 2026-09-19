-- Aufbewahrung beendeter Instanzen: Indizes fuer die instanzweise Loeschung.
--
-- Bewusst keine neuen Fremdschluessel auf {schema}.instances. Drei Gruende:
--
--  1. Ein `ON DELETE CASCADE` an einer Bestandstabelle scheitert am Anlegen, sobald darin auch
--     nur eine Zeile auf eine bereits entfernte Instanz zeigt. Genau das ist bei
--     idempotency_records der Normalfall: Der Verweis ueberlebt die Instanz absichtlich, und das
--     Loeschen eines Workflows entfernt seit jeher Instanzen ohne diese Verweise mitzunehmen.
--  2. runtime_node_events und user_task_assignment_events tragen ausdruecklich keinen
--     Fremdschluessel (siehe 010 und 012), damit eine technische Bereinigung die Spur nicht
--     unbemerkt mitnimmt. Die Aufbewahrung loescht sie ausdruecklich und nachlesbar in
--     InstancePurge — ein Cascade wuerde genau die Sichtbarkeit wieder aufgeben, die dort
--     gewollt ist.
--  3. Beide Ablagen muessen dieselbe Menge loeschen. Steht die Loeschreihenfolge in einer
--     Ablage im Schema und in der anderen im Code, laufen sie mit der Zeit auseinander.
--
-- Die Anmeldungen an user_task_subscriptions behalten dagegen ihr vorhandenes Cascade aus 006,
-- 007 und 008: Entwuerfe, Bearbeiterzustand, Faelligkeiten und Meldungen gehoeren zur Aufgabe,
-- nicht zur Instanz, und verschwinden weiterhin mit ihr.
--
-- Additiv und wiederholbar wie die uebrigen Migrationen.

-- Deckt DELETE ... WHERE process_instance_id = ? beim Loeschen einer Instanz ab. Der bestehende
-- ai_runs_instance_token_unique beginnt zwar mit derselben Spalte, ist aber ein Unique-Constraint
-- ueber zwei Spalten; ein eigener Index macht die Absicht sichtbar und bleibt unabhaengig davon.
CREATE INDEX IF NOT EXISTS ai_runs_instance_idx
    ON {schema}.ai_runs (process_instance_id);

-- idempotency_records wurde bisher nur ueber scope_hash und expires_at gelesen. Ohne Index
-- liefe jede Instanzloeschung in einen Full Scan dieser Tabelle.
CREATE INDEX IF NOT EXISTS idempotency_records_instance_idx
    ON {schema}.idempotency_records (process_instance_id)
    WHERE process_instance_id IS NOT NULL;
