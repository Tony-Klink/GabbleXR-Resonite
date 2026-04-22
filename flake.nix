{
  description = "GabbleXR_Resonite: OSC Receiver for Gabble";

  inputs = {
    nixpkgs.url = "github:nixos/nixpkgs?ref=nixos-unstable";
  };

  outputs = { self, nixpkgs }:
    let
      system = "x86_64-linux";
      pkgs = nixpkgs.legacyPackages.${system};
    in
      {
        devShells.${system}.default = pkgs.mkShell {
          buildInputs = with pkgs; [
            dotnetCorePackages.sdk_10_0-bin
            gemini-cli-bin
          ];

          shellHook = ''
          export XR_RUNTIME_JSON=/home/klink/.config/openxr/1/active_runtime.json
        '';
        };
      };
}
