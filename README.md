# TransacoesFinanceiras

## Visão Arquitetural

A solução é composta por uma API REST para transações financeiras e dois Worker Services distintos, seguindo arquitetura limpa e separação de responsabilidades:

- **TransactionApi**: expõe endpoints REST para criação e consulta de transações (PostgreSQL).
- **SettlementWorker**: processa eventos financeiros, garantindo consistência e idempotência das operações no PostgreSQL.
- **ProjectionWorker**: consome eventos e mantém projeções de leitura otimizadas no MongoDB.
- **Banco de Dados**:
  - **PostgreSQL**: armazena contas, transações e eventos de outbox.
  - **MongoDB**: armazena projeções para consultas rápidas.
- **BuildingBlocks**: abstrações e utilitários compartilhados.

## Decisões Técnicas

- **Outbox Pattern**:  
  Utilizado para garantir a entrega confiável de eventos, evitando perda de mensagens em caso de falhas entre a gravação no banco e o envio do evento. Eventos são gravados na tabela `Outbox` (PostgreSQL) e processados pelos Workers.

- **Idempotência**:  
  As operações de criação de transação são idempotentes, prevenindo duplicidade em caso de reenvio de requisições ou reprocessamento de eventos.

- **Concorrência**:  
  O controle de concorrência é realizado no nível do banco de dados (PostgreSQL), utilizando transações e locks para evitar condições de corrida, especialmente em operações de débito.

- **DLQ (Dead Letter Queue)**:  
  Eventos que falham repetidamente no processamento são movidos para uma fila de mensagens mortas (DLQ), permitindo análise e reprocessamento manual.

## Fluxo dos Eventos

1. **Criação de Transação**:  
   - O cliente envia uma requisição para a API.
   - A API valida e grava a transação e um evento na tabela Outbox (PostgreSQL).

2. **Processamento Assíncrono**:  
   - **SettlementWorker** lê eventos pendentes da Outbox no PostgreSQL, processa e publica eventos para sistemas externos ou filas.
   - **ProjectionWorker** consome eventos (por exemplo, de uma fila ou broker) e atualiza as projeções de leitura no MongoDB.
   - Eventos processados com sucesso são marcados como concluídos; falhas recorrentes vão para a DLQ.

## Como rodar com Docker Compose

1. Certifique-se de ter o Docker e Docker Compose instalados.
2. No diretório raiz do projeto, execute:
   ```sh
   docker-compose up --build
   ```
3. Acesse a API em `http://localhost:5000` (ou a porta configurada).


## Tutorial Docker Compose

O arquivo `docker-compose.yml` está localizado na raiz do projeto e orquestra todos os serviços necessários (API, workers, PostgreSQL, MongoDB, etc).

### Passo a passo

1. **Pré-requisitos**  
   - Docker e Docker Compose instalados em sua máquina.

2. **Configuração**  
   - Certifique-se de que as portas utilizadas nos containers não estão em uso por outros serviços locais.
   - Ajuste variáveis de ambiente no `docker-compose.yml` se necessário (por exemplo, senhas ou strings de conexão).

3. **Subindo o ambiente**  
   No terminal, na raiz do projeto, execute:
   ```sh
   docker-compose up --build
   ```
   Isso irá:
   - Construir as imagens dos projetos .NET e baixar as imagens dos bancos de dados.
   - Subir todos os containers necessários.

4. **Verificando os serviços**  
   - Aguarde até que todos os containers estejam com status “healthy”.
   - Acesse a API em `http://localhost:5000` (ou a porta configurada).
   - Os workers e bancos de dados estarão rodando em background.

5. **Parando o ambiente**  
   Para parar e remover todos os containers, execute:
   ```sh
   docker-compose down
   ```

## Como rodar localmente

1. Instale o .NET SDK (versão compatível com o projeto).
2. Configure as strings de conexão do PostgreSQL e MongoDB nos arquivos de configuração.
3. Execute as migrações do banco, se necessário.
4. Inicie a API:
   ```sh
   dotnet run --project TransactionApi
   ```
5. Inicie o SettlementWorker:
   ```sh
   dotnet run --project SettlementWorker
   ```
6. Inicie o ProjectionWorker:
   ```sh
   dotnet run --project ProjectionWorker
   ```

7. Acesse: `http://localhost:5150/swagger` para interagir com a API.

8. Para acompanhar os registro no banco de dados pode utilizar os comandos:
   ```sh
   PostgreSQL:
   docker exec -it finance_postgres psql -U postgres -d finance -c "SELECT \"EventType\", \"Processed\" FROM \"Outbox\" ORDER BY \"OccurredAt\" DESC LIMIT 5;"
   
   MongoDB:
   docker exec -it finance_mongo mongosh finance-read --eval \
   'db.account_statement.find().sort({ LastUpdated: -1 }).limit(5)'
   
   Kafka:
   docker exec -it finance_kafka bash -lc "kafka-console-consumer --bootstrap-server localhost:9092 --topic transaction-settled --from-beginning --timeout-ms 5000"
   ```
## Estrutura do Projeto

```
/
├── TransactionApi/                # API de transações (PostgreSQL)
│   ├── Controllers/
│   ├── Application/
│   ├── Domain/
│   └── ...
├── SettlementWorker/              # Worker para processamento de transações (PostgreSQL)
├── ProjectionWorker/              # Worker para projeções de leitura (MongoDB)
├── BuildingBlocks/                # Componentes compartilhados (Entidades, Infraestrutura)
├── TransactionApi.Tests/          # Testes da API
├── Workers.Tests/                 # Testes dos Workers
├── docker-compose.yml             # Orquestração dos serviços
└── README.md
```

## Testes

- Os testes estão localizados nas pastas `TransactionApi.Tests` e `Workers.Tests`.
- Para rodar todos os testes:
  ```sh
  dotnet test
  ```
- Os testes cobrem:
  - Validações de regras de negócio (ex: saldo insuficiente, tipos de transação)
  - Persistência e integridade dos dados
  - Fluxo de eventos e integração com Outbox
