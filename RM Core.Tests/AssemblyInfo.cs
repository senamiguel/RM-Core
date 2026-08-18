using Xunit;

// Testes E2E que sobem um WPF nao podem rodar em paralelo - cada
// teste sobe e derruba seu proprio processo do RM Core, e manter
// 1 thread evita contencao de CPU/dispatcher durante o caos.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
