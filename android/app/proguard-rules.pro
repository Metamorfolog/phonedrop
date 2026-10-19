# Skener QR kódu (ML Kit přes Play služby) si své součásti dohledává za běhu podle názvů v manifestu;
# R8 by je jinak zahodil a skener by při spuštění spadl.
-keep class com.google.mlkit.** { *; }
-keep class com.google.android.gms.internal.mlkit_code_scanner.** { *; }
-keep class com.google.android.gms.internal.mlkit_vision_barcode.** { *; }
-keep class * implements com.google.firebase.components.ComponentRegistrar { *; }
