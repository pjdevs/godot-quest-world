# First-person camera must clear the imported head

## Pitfall

The shared camera pivot is near eye height, but the imported UAL head occupies that pivot volume. With a zero first-person camera offset, the camera sits inside/behind the head and the head mesh visibly intersects the view while turning or moving.

## Rule

Keep the camera hierarchy independent from `Visual`, keep the third-person spring-arm position unchanged, and apply a small first-person offset along the camera's local `-Z` axis to the `SpringArm3D` pivot. The current default is `Vector3(0, 0, -0.2)`, which places the FPS camera just in front of the mannequin's head.
