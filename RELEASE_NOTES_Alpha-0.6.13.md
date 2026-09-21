# 🚀 RM Core vAlpha-0.6.13

Esta versão traz a resolução definitiva para a persistência de clientes e bases no SQLite, auto-migração transparente de schema sem perda de dados, validação rigorosa de integridade com feedback visual na interface e correções de navegação no EF Core 9.

---

### 🌟 Principais Correções e Melhorias

#### 🗄️ Persistência SQLite & Auto-Migração (BUG-36)
- **Auto-Migração de Schema (`EnsureDatabaseMigrated`)**: O banco de dados local inspeciona a estrutura de tabelas via `PRAGMA table_info` e adiciona dinamicamente qualquer coluna faltante (`DelBroker`, `VerboseLogs`, `ApagarHost`, `DbUser`, `DbPass`, `RmVersion`, etc.) ao reutilizar arquivos `rmcore.db` de versões anteriores.
- **Eliminação de Colunas Órfãs e Constraints Incompatíveis**: Tratamento e reconstrução segura de tabelas para expurgar colunas legadas com restrições que causavam conflitos (como a restrição `ControlaIIS NOT NULL` que gerava `SQLite Error 19: NOT NULL constraint failed: Ambientes.ControlaIIS`).
- **Navegações Opcionais Seguras (EF Core 9)**: Propriedades de navegação 1:1 e 1:N entre `Ambiente`, `AmbienteConfig` e `AliasModel` foram ajustadas com `IsRequired(false)` e anotação anulável (`Ambiente?`), permitindo inserções parciais por chave estrangeira sem bloqueio do change tracker.

#### 🛡️ Integridade de Dados & Feedback Visual
- **Feedback Imediato na Interface**: As operações de salvar perfis (`SaveProfiles`) e bases (`SaveAliases`) agora validam explicitamente o resultado retornado pelo SQLite em transação. Em caso de falha, um alerta informativo é exibido na interface com a mensagem de causa raiz (`ex.InnerException`), eliminando perda silenciosa de dados.
- **Rastreamento de Mudanças em Memória**: As entidades recém-adicionadas são registradas ativamente no contexto local do EF Core após cada inserção, prevenindo duplicações de chave primária e inconsistências de estado entre telas.

#### ⚡ Estabilidade & Confiabilidade
- **Startup Confiável**: A rotina de migração executa de forma automática e transparente no construtor da janela principal antes do carregamento de perfis e conexões.
- **Relatório de Bugs Atualizado**: Documentado o `BUG-36` em detalhes no [`RELATORIO_DE_BUGS_E_CORRECOES.md`](file:///c:/Users/MIGUEL.SENA/Documents/RM_Core/RELATORIO_DE_BUGS_E_CORRECOES.md).

---

### 📦 Arquivos da Release
* **Instalador Oficial**: `RM-Core-Setup-Alpha-0.6.13.exe`

---

### 🔧 Como Atualizar
1. Baixe o instalador **`RM-Core-Setup-Alpha-0.6.13.exe`** anexado nesta release.
2. Execute o instalador (ele fechará instâncias abertas e atualizará os binários em `Program Files\RM_CORE`).
3. Todos os seus clientes, bases e preferências serão preservados e migrados automaticamente.

---

**Full Changelog**: https://github.com/senamiguel/RM-Core/compare/Alpha-0.6.12...Alpha-0.6.13
