# How to run

- Clone this repository.

- Open a terminal in the directory of the cloned project

- If rabbitMQ is not running yet. Ps: the main application already starts the service.

```bash
docker compose up -d
```

- Then

```bash
dotnet run 1
```

- If you need other consumer

```bash
dotnet run 2
```