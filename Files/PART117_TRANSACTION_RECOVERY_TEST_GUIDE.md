# Transaction Center test guide

1. Execute a supported low-risk action from AI Decision Center or Execution Center.
2. Open AI & Automation > Transactions.
3. Confirm the transaction contains Started and Committed or Failed events.
4. Use API Logs and Evidence buttons to inspect the correlated WordPress result.
5. To simulate interruption in a test copy, append only a Started event and set its UTC time more than ten minutes in the past. Refresh the journal and confirm the state is Interrupted.
6. Reconcile selected records a RecoveryReview event only; it never repeats the WordPress write.
