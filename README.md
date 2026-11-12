# CameraControl • Sistema Cliente-Servidor Padre/Hijos (Onion Architecture + Docker)

Prototipo funcional y modular en **C# (.NET 10)** diseñado bajo **Onion Architecture** para automatizar la consulta, aprovisionamiento dinámico y captura de imágenes/datos en formato JSON desde múltiples cámaras mediante una topología de **Servidor Padre (Orquestador)** y **Servidores Hijos (Nodos de Cámara)**.

---

## 🏛️ Estructura Onion Architecture

```text
CameraControl/
├── CameraControl.sln
├── docker-compose.yml               # Red bridge 10.10.0.0/24 + Servidor Padre (10.10.0.2) + Hijos iniciales
├── Dockerfile.parent                # Imagen del Servidor Padre Orquestador
├── Dockerfile.child                 # Imagen base de los Servidores Hijos (cameracontrol-child:latest)
└── src/
    ├── CameraControl.Domain/        # Núcleo de Dominio (Sin dependencias externas)
    │   ├── Entities/
    │   │   ├── ChildServerNode.cs   # Entidad Hijo con validación estricta de IPs 10.x.x.x y 127.x.x.x
    │   │   └── CameraCaptureResult.cs
    │   └── Ports/
    │       └── IDomainPorts.cs      # Contratos: IChildNodeRepository, IChildNodeProvisioner, IChildNodeCommunicator, ICameraHardwareAdapter
    ├── CameraControl.Application/   # Casos de Uso y Orquestación
    │   ├── DTOs/
    │   ├── Interfaces/
    │   └── Services/
    │       ├── ParentOrchestratorService.cs
    │       ├── ChildCameraNodeService.cs
    │       └── AutoPollingStateService.cs
    ├── CameraControl.Infrastructure/# Adaptadores de Infraestructura
    │   ├── Camera/
    │   │   └── SimulatedCameraHardwareAdapter.cs # Generador de fotograma Base64 + JSON de cámara
    │   ├── Communication/
    │   │   └── HttpChildNodeCommunicator.cs      # Cliente HTTP Padre -> Hijo con cabeceras de identidad del Padre
    │   ├── Persistence/
    │   │   └── InMemoryRepositories.cs
    │   └── Provisioning/
    │       └── HybridChildNodeProvisioner.cs     # Crea contenedores hijos vía Docker API o instancias Kestrel en loopback
    ├── CameraControl.ParentServer/  # Web API + Dashboard Web + Background Worker de Poleo Automático
    └── CameraControl.ChildServer/   # Microservicio Servidor Hijo independiente por cámara
```

---

## 🚀 Cómo Ejecutar el Prototipo

### Opción A: Con Docker Compose (Simulación Completa de Red `10.10.0.0/24` + Docker Engine API)

1. En la raíz del proyecto, ejecuta:
   ```bash
   docker compose up --build
   ```
2. Esto levantará:
   - **Red Docker `camera_net`** (`10.10.0.0/24`)
   - **Servidor Padre (`camera-parent-server`)** en la IP `10.10.0.2` (expuesto en tu máquina en `http://localhost:5000`)
   - **Servidor Hijo 1 (`child-cam-01`)** en la IP `10.10.0.11` respondiendo a `10.10.0.2`
   - **Servidor Hijo 2 (`child-cam-02`)** en la IP `10.10.0.12` respondiendo a `10.10.0.2`
3. Abre el **Dashboard del Servidor Padre** en:
   - 👉 `http://localhost:5000`
4. Desde el Dashboard (o vía `POST /api/parent/nodes`), cuando crees un nuevo hijo con IP `10.10.0.13`, `10.10.0.14`, etc., el Servidor Padre invocará `/var/run/docker.sock` (Docker Engine API) para **crear e iniciar un nuevo contenedor hijo real** con esa IP asignada en `camera_net`.

### Opción B: Ejecución Directa Local con .NET CLI (Sin requerir Docker corriendo)

1. Ejecuta el Servidor Padre:
   ```bash
   dotnet run --project src/CameraControl.ParentServer/CameraControl.ParentServer.csproj
   ```
2. Abre `http://localhost:5000`.
3. En modo local sin socket de Docker, el `HybridChildNodeProvisioner` levanta automáticamente **servidores HTTP Kestrel hijos dedicados en segundo plano**, permitiéndote crear hijos con IPs `10.x.x.x` o `127.x.x.x` y probar todo el flujo Cliente-Servidor de inmediato.

---

## 🔌 Cómo Integrar la App Existente de las Cámaras en el Servidor Real

Gracias a la **Arquitectura Onion**, toda la interacción con la cámara o con el aplicativo legado que ya desarrollaron en tu servicio está encapsulada en una única interfaz del Dominio:

- Interfaz: `src/CameraControl.Domain/Ports/IDomainPorts.cs` -> `ICameraHardwareAdapter`
- Implementación actual: `src/CameraControl.Infrastructure/Camera/SimulatedCameraHardwareAdapter.cs`

Para conectarlo con la aplicación real en producción, solo debes reemplazar o modificar `CaptureSnapshotAsync` en `SimulatedCameraHardwareAdapter.cs` (o crear `RealCameraHardwareAdapter : ICameraHardwareAdapter`) para invocar el socket/proceso/HTTP de su aplicación de cámara usando el parámetro `cameraIp`.
