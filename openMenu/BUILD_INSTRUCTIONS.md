# Building openMenu

Install Docker Desktop, or Docker Engine on Linux. Start Docker before building.
The first setup needs an internet connection and can take a while.

## Command line (Linux or WSL)

Open a terminal in the `openMenu` folder and run:

```sh
docker build --platform linux/amd64 -t openmenu-build ./docker
docker run --rm -it --platform linux/amd64 \
  --user "$(id -u):$(id -g)" \
  -v "$PWD:/workspaces/openmenu" -w /workspaces/openmenu \
  openmenu-build bash
```

Inside the container, run:

```sh
source /opt/toolchains/dc/kos/environ.sh
cmake --preset dc-release
cmake --build --preset dc-release
```

Type `exit` to leave the container.

## VS Code (optional)

Install Visual Studio Code and its Dev Containers extension.

1. Start Docker.
2. Open the `openMenu` folder in VS Code.
3. Open the Command Palette and select **Dev Containers: Reopen in Container**.
4. Wait for setup to finish. This downloads and builds the compiler and KOS, and
   applies the included patches. The first run can take a while.
5. Open a terminal in VS Code and run:

   ```sh
   cmake --preset dc-release
   cmake --build --preset dc-release
   ```

## Output

The finished file is `cmake-build-dc-release/bin/1ST_READ.BIN` in your source folder.

After changing the source, run `cmake --build --preset dc-release` again inside
the container.
