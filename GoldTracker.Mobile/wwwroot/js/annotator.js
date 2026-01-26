
var annotator = {
    instances: new Map(),

    init: function (canvas, image, dotNetHelper, readOnly = false, showBoxes = true) {
        if (!canvas) return;
        const id = canvas.id || 'default-canvas';

        // Destroy existing if any
        if (this.instances.has(id)) {
            this.destroy(id);
        }

        const instance = {
            canvas: canvas,
            image: image,
            dotNetHelper: dotNetHelper,
            ctx: canvas.getContext('2d'),
            selectedBoxIndex: -1,
            isDraggingHandle: false,
            activeHandle: null,
            isRefining: false,
            magnifierSize: 110,
            magnifierZoom: 3,
            boxes: [],
            readOnly: readOnly,
            showBoxes: showBoxes,
            squareEnforced: true,
            isDrawing: false,

            resizeCanvas: function () {
                this.canvas.width = this.image.clientWidth;
                this.canvas.height = this.image.clientHeight;
                this.redraw();
            },

            getCanvasCoordinates: function (e) {
                const rect = this.canvas.getBoundingClientRect();
                let clientX, clientY;
                if (e.touches && e.touches.length > 0) {
                    clientX = e.touches[0].clientX; clientY = e.touches[0].clientY;
                } else if (e.changedTouches && e.changedTouches.length > 0) {
                    clientX = e.changedTouches[0].clientX; clientY = e.changedTouches[0].clientY;
                } else {
                    clientX = e.clientX; clientY = e.clientY;
                }
                return {
                    x: (clientX - rect.left) * (this.canvas.width / rect.width),
                    y: (clientY - rect.top) * (this.canvas.height / rect.height)
                };
            },

            handleStart: function (e) {
                if (e.cancelable) e.preventDefault();
                const pos = this.getCanvasCoordinates(e);

                // 1. Check if touching magnifier area for refinement
                if (this.selectedBoxIndex !== -1 && this.isInsideMagnifier(pos)) {
                    this.isRefining = true;
                    this.lastRefinePos = pos;
                    return;
                }

                if (!this.readOnly) {
                    // 2. Check if touching handles of selected box
                    if (this.selectedBoxIndex !== -1) {
                        const handle = this.getHandleAt(pos, this.boxes[this.selectedBoxIndex]);
                        if (handle) {
                            this.isDraggingHandle = true;
                            this.activeHandle = handle;
                            this.lastPos = pos;
                            return;
                        }
                    }
                }

                // 3. Check if clicking on any existing box to select it
                const clickedBoxIndex = this.getBoxAtPoint(pos);
                if (clickedBoxIndex !== -1) {
                    this.selectedBoxIndex = clickedBoxIndex;
                    this.activeHandle = this.readOnly ? null : 'center';
                    this.redraw();
                    if (this.dotNetHelper) {
                        this.dotNetHelper.invokeMethodAsync('OnBoxSelected', clickedBoxIndex);
                    }
                    return;
                }

                // 4. Clicked on empty space - Deselect if not read-only
                if (!this.readOnly) {
                    if (this.selectedBoxIndex !== -1) {
                        this.selectedBoxIndex = -1;
                        this.redraw();
                        if (this.dotNetHelper) {
                            this.dotNetHelper.invokeMethodAsync('OnBoxSelected', -1);
                        }
                    }

                    // 5. Otherwise, start drawing a new box
                    this.isDrawing = true;
                    this.startX = pos.x;
                    this.startY = pos.y;
                    this.redraw();
                }
            },

            handleMove: function (e) {
                if (e.cancelable) e.preventDefault();
                const pos = this.getCanvasCoordinates(e);

                if (this.isRefining) {
                    this.refineBox(pos);
                    return;
                }

                if (this.isDraggingHandle) {
                    this.moveHandle(pos);
                    return;
                }

                if (this.isDrawing) {
                    this.drawPreview(pos);
                    return;
                }
            },

            handleEnd: function (e) {
                if (e.cancelable) e.preventDefault();

                if (this.isRefining) {
                    this.isRefining = false;
                    this.activeHandle = null;
                    this.redraw();
                    this.notifyUpdate();
                    return;
                }

                if (this.isDraggingHandle) {
                    this.isDraggingHandle = false;
                    this.activeHandle = null;
                    this.redraw();
                    this.notifyUpdate();
                    return;
                }

                if (this.isDrawing) {
                    this.isDrawing = false;
                    const pos = this.getCanvasCoordinates(e);

                    const dx = pos.x - this.startX;
                    const dy = pos.y - this.startY;
                    let endX = pos.x;
                    let endY = pos.y;

                    const absDx = Math.abs(dx);
                    const absDy = Math.abs(dy);

                    if (this.squareEnforced) {
                        const side = Math.max(absDx, absDy);
                        endX = this.startX + (dx >= 0 ? side : -side);
                        endY = this.startY + (dy >= 0 ? side : -side);
                    }

                    // Only notify .NET, don't add locally to avoid duplication
                    if (absDx > 5 || absDy > 5) {
                        if (this.dotNetHelper) {
                            const startX = Math.min(this.startX, endX) / this.canvas.width;
                            const startY = Math.min(this.startY, endY) / this.canvas.height;
                            const finalEndX = Math.max(this.startX, endX) / this.canvas.width;
                            const finalEndY = Math.max(this.startY, endY) / this.canvas.height;
                            this.dotNetHelper.invokeMethodAsync('AddBox', startX, startY, finalEndX, finalEndY);
                        }
                    }
                    this.redraw();
                }
            },

            notifyUpdate: function () {
                if (this.selectedBoxIndex !== -1 && this.dotNetHelper && !this.readOnly) {
                    const box = this.boxes[this.selectedBoxIndex];
                    this.dotNetHelper.invokeMethodAsync('UpdateBox',
                        this.selectedBoxIndex,
                        box.startX, box.startY, box.endX, box.endY);
                }
            },

            redraw: function () {
                this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);

                this.boxes.forEach((box, index) => {
                    const isSelected = index === this.selectedBoxIndex;
                    // Draw box if it's not hidden OR if it's selected
                    if ((this.showBoxes && !box.hidden) || isSelected) {
                        this.drawBox(box, isSelected);
                    }
                });

                if (this.selectedBoxIndex !== -1) {
                    this.drawMagnifier();
                }
            },

            drawBox: function (box, isSelected) {
                if (box.hidden) return;
                const x = box.startX * this.canvas.width;
                const y = box.startY * this.canvas.height;
                const w = (box.endX - box.startX) * this.canvas.width;
                const h = (box.endY - box.startY) * this.canvas.height;

                this.ctx.save();
                this.ctx.strokeStyle = box.color || '#FF0000';
                this.ctx.lineWidth = isSelected ? 3 : 2;

                // Draw circle (ellipse)
                // If it's an arrow (label != target), ALWAYS enforce circle rendering.
                // squareEnforced is for interaction, but visual rendering should always be circular for arrows
                this.ctx.beginPath();
                if (box.label && box.label.toLowerCase() !== 'target') {
                    const radius = Math.min(Math.abs(w), Math.abs(h)) / 2;
                    // Use center + avg radius for best visual fit
                    this.ctx.arc(x + w / 2, y + h / 2, radius, 0, 2 * Math.PI);
                } else {
                    this.ctx.ellipse(x + w / 2, y + h / 2, Math.abs(w / 2), Math.abs(h / 2), 0, 0, 2 * Math.PI);
                }
                this.ctx.stroke();

                if (isSelected && !this.readOnly) {
                    this.drawHandles(x, y, w, h);
                }

                // Enhanced Label with Background Pill
                if (box.label) {
                    const labelText = box.label === 'target' ? 'TARGET' : box.label;
                    this.ctx.font = 'bold 14px sans-serif';
                    const textWidth = this.ctx.measureText(labelText).width;
                    const paddingH = 6;
                    const paddingV = 4;
                    const pillW = textWidth + (paddingH * 2);
                    const pillH = 18;
                    const pillX = x + (w / 2) - (pillW / 2);
                    const pillY = y - pillH - 8;

                    // Draw pill shadow
                    this.ctx.shadowBlur = 4;
                    this.ctx.shadowColor = 'rgba(0,0,0,0.3)';

                    // Draw white pill background
                    this.ctx.fillStyle = '#FFFFFF';
                    this.ctx.beginPath();
                    this.ctx.roundRect(pillX, pillY, pillW, pillH, pillH / 2);
                    this.ctx.fill();

                    this.ctx.shadowBlur = 0; // Reset shadow

                    // Draw label text
                    this.ctx.fillStyle = (box.label === 'target') ? '#FF4081' : (box.color || '#333');
                    this.ctx.textAlign = 'center';
                    this.ctx.textBaseline = 'middle';
                    this.ctx.fillText(labelText, pillX + (pillW / 2), pillY + (pillH / 2) + 1);
                }
                this.ctx.restore();
            },

            drawHandles: function (x, y, w, h) {
                const size = 10;
                this.ctx.fillStyle = '#FFFFFF';
                this.ctx.strokeStyle = '#000000';
                this.ctx.lineWidth = 1;

                const handles = [
                    // Corners
                    { x: x, y: y }, { x: x + w, y: y },
                    { x: x, y: y + h }, { x: x + w, y: y + h },
                    // Edges
                    { x: x + w / 2, y: y }, { x: x + w / 2, y: y + h },
                    { x: x, y: y + h / 2 }, { x: x + w, y: y + h / 2 }
                ];

                handles.forEach(p => {
                    this.ctx.fillRect(p.x - size / 2, p.y - size / 2, size, size);
                    this.ctx.strokeRect(p.x - size / 2, p.y - size / 2, size, size);
                });
            },

            drawMagnifier: function () {
                const box = this.boxes[this.selectedBoxIndex];
                const centerX = (box.startX + box.endX) / 2 * this.canvas.width;
                const centerY = (box.startY + box.endY) / 2 * this.canvas.height;

                this.ctx.save();

                // Magnifier position (top right or top left depending on box position)
                let magX = this.canvas.width - this.magnifierSize - 10;
                let magY = 10;
                if (centerX > this.canvas.width / 2 && centerY < this.canvas.height / 2) {
                    magX = 10; // Move to left if box is in top right
                }

                // Draw magnifier background
                this.ctx.beginPath();
                this.ctx.arc(magX + this.magnifierSize / 2, magY + this.magnifierSize / 2, this.magnifierSize / 2, 0, Math.PI * 2);
                this.ctx.clip();
                this.ctx.fillStyle = '#000';
                this.ctx.fillRect(magX, magY, this.magnifierSize, this.magnifierSize);

                // Draw zoomed image
                const zoom = this.magnifierZoom;
                const sourceSize = this.magnifierSize / zoom;

                this.ctx.drawImage(this.image,
                    (centerX / this.canvas.width) * this.image.naturalWidth - (sourceSize / 2 / this.canvas.width) * this.image.naturalWidth,
                    (centerY / this.canvas.height) * this.image.naturalHeight - (sourceSize / 2 / this.canvas.height) * this.image.naturalHeight,
                    (sourceSize / this.canvas.width) * this.image.naturalWidth,
                    (sourceSize / this.canvas.height) * this.image.naturalHeight,
                    magX, magY, this.magnifierSize, this.magnifierSize);

                // Draw crosshair in magnifier
                this.ctx.strokeStyle = 'cyan';
                this.ctx.lineWidth = 1;
                this.ctx.beginPath();
                this.ctx.moveTo(magX + this.magnifierSize / 2, magY);
                this.ctx.lineTo(magX + this.magnifierSize / 2, magY + this.magnifierSize);
                this.ctx.moveTo(magX, magY + this.magnifierSize / 2);
                this.ctx.lineTo(magX + this.magnifierSize, magY + this.magnifierSize / 2);
                this.ctx.stroke();

                this.ctx.restore();

                // Draw magnifier border
                this.ctx.arc(magX + this.magnifierSize / 2, magY + this.magnifierSize / 2, this.magnifierSize / 2, 0, Math.PI * 2);
                this.ctx.stroke();

                // Draw label in magnifier
                if (box.label && box.label !== 'target') {
                    this.ctx.font = 'bold 14px Arial';
                    const textWidth = this.ctx.measureText(box.label).width;
                    const bgWidth = textWidth + 10;

                    this.ctx.fillStyle = 'rgba(0,0,0,0.6)';
                    this.ctx.fillRect(magX + 5, magY + 5, bgWidth, 20);

                    this.ctx.fillStyle = 'cyan';
                    this.ctx.textBaseline = 'top';
                    this.ctx.fillText(box.label, magX + 10, magY + 8);
                }
            },

            isInside: function (pos, box) {
                const x = box.startX * this.canvas.width;
                const y = box.startY * this.canvas.height;
                const w = (box.endX - box.startX) * this.canvas.width;
                const h = (box.endY - box.startY) * this.canvas.height;
                const cx = x + w / 2;
                const cy = y + h / 2;
                const rx = Math.abs(w / 2);
                const ry = Math.abs(h / 2);

                const dx = pos.x - cx;
                const dy = pos.y - cy;

                // Elliptical hit test: (x/rx)^2 + (y/ry)^2 <= 1
                return (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) <= 1.0;
            },

            isNearEdge: function (pos, box) {
                const x = box.startX * this.canvas.width;
                const y = box.startY * this.canvas.height;
                const w = (box.endX - box.startX) * this.canvas.width;
                const h = (box.endY - box.startY) * this.canvas.height;

                const cx = x + w / 2;
                const cy = y + h / 2;
                const dx = pos.x - cx;
                const dy = pos.y - cy;
                const dist = Math.sqrt(dx * dx + dy * dy);

                const r = (Math.abs(w / 2) + Math.abs(h / 2)) / 2;
                // Precise selection: max 20px or 10% of radius
                const tolerance = Math.min(20, Math.max(10, r * 0.1));

                return Math.abs(dist - r) < tolerance;
            },

            getBoxAtPoint: function (pos) {
                let minArea = Infinity;
                let minIndex = -1;

                for (let i = 0; i < this.boxes.length; i++) {
                    // Only select by edge to allow drawing inside larger objects
                    if (this.isNearEdge(pos, this.boxes[i])) {
                        const w = (this.boxes[i].endX - this.boxes[i].startX) * this.canvas.width;
                        const h = (this.boxes[i].endY - this.boxes[i].startY) * this.canvas.height;
                        const area = Math.abs(w * h);

                        if (area < minArea) {
                            minArea = area;
                            minIndex = i;
                        }
                    }
                }
                return minIndex;
            },

            isInsideMagnifier: function (pos) {
                if (this.selectedBoxIndex === -1) return false;
                const box = this.boxes[this.selectedBoxIndex];
                const centerX = (box.startX + box.endX) / 2 * this.canvas.width;
                const centerY = (box.startY + box.endY) / 2 * this.canvas.height;

                let magX = this.canvas.width - this.magnifierSize - 10;
                let magY = 10;
                if (centerX > this.canvas.width / 2 && centerY < this.canvas.height / 2) magX = 10;

                const dx = pos.x - (magX + this.magnifierSize / 2);
                const dy = pos.y - (magY + this.magnifierSize / 2);
                return Math.sqrt(dx * dx + dy * dy) <= this.magnifierSize / 2;
            },

            getHandleAt: function (pos, box) {
                const x = box.startX * this.canvas.width;
                const y = box.startY * this.canvas.height;
                const w = (box.endX - box.startX) * this.canvas.width;
                const h = (box.endY - box.startY) * this.canvas.height;
                const size = 20;

                // Corner handles
                if (Math.abs(pos.x - x) < size && Math.abs(pos.y - y) < size) return 'tl';
                if (Math.abs(pos.x - (x + w)) < size && Math.abs(pos.y - y) < size) return 'tr';
                if (Math.abs(pos.x - x) < size && Math.abs(pos.y - (y + h)) < size) return 'bl';
                if (Math.abs(pos.x - (x + w)) < size && Math.abs(pos.y - (y + h)) < size) return 'br';

                // Edge handles (for cut-off targets)
                if (Math.abs(pos.y - y) < size && pos.x > x && pos.x < x + w) return 't';
                if (Math.abs(pos.y - (y + h)) < size && pos.x > x && pos.x < x + w) return 'b';
                if (Math.abs(pos.x - x) < size && pos.y > y && pos.y < y + h) return 'l';
                if (Math.abs(pos.x - (x + w)) < size && pos.y > y && pos.y < y + h) return 'r';

                return null;
            },

            moveHandle: function (pos) {
                if (this.selectedBoxIndex === -1 || this.readOnly) return;
                const box = this.boxes[this.selectedBoxIndex];

                let nx = pos.x;
                let ny = pos.y;
                let fixX, fixY;

                // Determine fixed points based on handle
                if (this.activeHandle === 'tl') { fixX = box.endX * this.canvas.width; fixY = box.endY * this.canvas.height; }
                else if (this.activeHandle === 'tr') { fixX = box.startX * this.canvas.width; fixY = box.endY * this.canvas.height; }
                else if (this.activeHandle === 'bl') { fixX = box.endX * this.canvas.width; fixY = box.startY * this.canvas.height; }
                else if (this.activeHandle === 'br') { fixX = box.startX * this.canvas.width; fixY = box.startY * this.canvas.height; }
                else if (this.activeHandle === 't') { fixY = box.endY * this.canvas.height; }
                else if (this.activeHandle === 'b') { fixY = box.startY * this.canvas.height; }
                else if (this.activeHandle === 'l') { fixX = box.endX * this.canvas.width; }
                else if (this.activeHandle === 'r') { fixX = box.startX * this.canvas.width; }

                // Apply square enforcement if needed
                if (this.squareEnforced && box.label !== 'target') {
                    if (['tl', 'tr', 'bl', 'br'].includes(this.activeHandle)) {
                        const dx = nx - fixX;
                        const dy = ny - fixY;
                        const side = Math.max(Math.abs(dx), Math.abs(dy));
                        nx = fixX + (dx >= 0 ? side : -side);
                        ny = fixY + (dy >= 0 ? side : -side);
                    }
                    // Edge resizing doesn't easily support square enforcement without shifting center,
                    // but for targets (where square is false) it's perfect.
                }

                // Update box coordinates
                if (this.activeHandle === 'tl') { box.startX = nx / this.canvas.width; box.startY = ny / this.canvas.height; }
                else if (this.activeHandle === 'tr') { box.endX = nx / this.canvas.width; box.startY = ny / this.canvas.height; }
                else if (this.activeHandle === 'bl') { box.startX = nx / this.canvas.width; box.endY = ny / this.canvas.height; }
                else if (this.activeHandle === 'br') { box.endX = nx / this.canvas.width; box.endY = ny / this.canvas.height; }
                else if (this.activeHandle === 't') { box.startY = ny / this.canvas.height; }
                else if (this.activeHandle === 'b') { box.endY = ny / this.canvas.height; }
                else if (this.activeHandle === 'l') { box.startX = nx / this.canvas.width; }
                else if (this.activeHandle === 'r') { box.endX = nx / this.canvas.width; }

                this.redraw();
            },

            refineBox: function (pos) {
                if (this.selectedBoxIndex === -1 || this.readOnly) return;
                const box = this.boxes[this.selectedBoxIndex];

                const dx = (pos.x - this.lastRefinePos.x) / this.canvas.width / this.magnifierZoom;
                const dy = (pos.y - this.lastRefinePos.y) / this.canvas.height / this.magnifierZoom;

                box.startX += dx;
                box.endX += dx;
                box.startY += dy;
                box.endY += dy;

                this.lastRefinePos = pos;
                this.redraw();
            },

            drawPreview: function (pos) {
                if (this.readOnly) return;
                this.redraw();
                const dx = pos.x - this.startX;
                const dy = pos.y - this.startY;
                let endX = pos.x;
                let endY = pos.y;

                if (this.squareEnforced) {
                    const side = Math.max(Math.abs(dx), Math.abs(dy));
                    endX = this.startX + (dx >= 0 ? side : -side);
                    endY = this.startY + (dy >= 0 ? side : -side);
                }

                const x = Math.min(this.startX, endX);
                const y = Math.min(this.startY, endY);
                const w = Math.abs(endX - this.startX);
                const h = Math.abs(endY - this.startY);

                this.ctx.strokeStyle = '#FF0000';
                this.ctx.setLineDash([5, 5]);
                this.ctx.strokeRect(x, y, w, h);
                this.ctx.setLineDash([]);
            }
        };

        const onResize = instance.resizeCanvas.bind(instance);
        window.addEventListener('resize', onResize);
        instance.onResize = onResize;

        const handleStartBound = instance.handleStart.bind(instance);
        const handleMoveBound = instance.handleMove.bind(instance);
        const handleEndBound = instance.handleEnd.bind(instance);

        canvas.addEventListener('mousedown', handleStartBound);
        canvas.addEventListener('mousemove', handleMoveBound);
        canvas.addEventListener('mouseup', handleEndBound);
        canvas.addEventListener('mouseleave', handleEndBound);

        canvas.addEventListener('touchstart', (e) => handleStartBound(e));
        canvas.addEventListener('touchmove', (e) => handleMoveBound(e));
        canvas.addEventListener('touchend', (e) => handleEndBound(e));

        this.instances.set(id, instance);
        instance.resizeCanvas();
    },

    get: function (id) {
        return this.instances.get(id || 'default-canvas');
    },

    loadBoxes: function (canvasId, boxesArray) {
        const inst = this.instances.get(canvasId);
        if (inst) {
            inst.boxes = boxesArray.map(b => ({
                startX: b.startX,
                startY: b.startY,
                endX: b.endX,
                endY: b.endY,
                label: b.label,
                color: b.color,
                hidden: b.hidden
            }));
            inst.selectedBoxIndex = -1;
            inst.redraw();
        }
    },

    removeBox: function (canvasId, index) {
        const inst = this.instances.get(canvasId);
        if (inst) {
            if (index >= 0 && index < inst.boxes.length) {
                inst.boxes.splice(index, 1);
                if (inst.selectedBoxIndex === index) {
                    inst.selectedBoxIndex = -1;
                } else if (inst.selectedBoxIndex > index) {
                    inst.selectedBoxIndex--;
                }
                inst.redraw();
            }
        }
    },

    removeLastBox: function (canvasId) {
        const inst = this.instances.get(canvasId || 'default-canvas');
        if (inst && inst.boxes.length > 0) {
            inst.boxes.pop();
            inst.selectedBoxIndex = -1;
            inst.redraw();
        }
    },

    updateBoxStyle: function (canvasId, index, color, label) {
        const inst = this.instances.get(canvasId);
        if (inst) {
            if (index >= 0 && index < inst.boxes.length) {
                inst.boxes[index].color = color;
                inst.boxes[index].label = label;
                inst.redraw();
            }
        }
    },

    selectBox: function (canvasId, index) {
        const inst = this.instances.get(canvasId);
        if (inst) {
            if (index >= 0 && index < inst.boxes.length) {
                inst.selectedBoxIndex = index;
                inst.activeHandle = inst.readOnly ? null : 'center';
                inst.redraw();
                return true;
            }
            inst.selectedBoxIndex = -1;
            inst.redraw();
        }
        return false;
    },

    addBox: function (canvasId, box) {
        const inst = this.instances.get(canvasId);
        if (inst) {
            inst.boxes.push({
                startX: box.startX,
                startY: box.startY,
                endX: box.endX,
                endY: box.endY,
                label: box.label,
                color: box.color,
                hidden: box.hidden
            });
            inst.selectedBoxIndex = inst.boxes.length - 1;
            inst.activeHandle = inst.readOnly ? null : 'center';
            inst.redraw();
        }
    },

    deselect: function (id) {
        const inst = this.instances.get(id || 'default-canvas');
        if (inst) {
            inst.selectedBoxIndex = -1;
            inst.redraw();
        }
    },

    clear: function (id) {
        const inst = this.instances.get(id);
        if (inst) {
            inst.boxes = [];
            inst.selectedBoxIndex = -1;
            inst.redraw();
        }
    },

    setSquareEnforced: function (id, enforced) {
        const inst = this.instances.get(id);
        if (inst) {
            inst.squareEnforced = enforced;
        }
    },

    destroy: function (id) {
        const inst = this.instances.get(id);
        if (inst) {
            window.removeEventListener('resize', inst.onResize);
            // In Blazor, don't replace the canvas or it breaks @ref
            // Only remove listeners if possible, but cloning is safer for standard JS
            // For now, let's just delete the instance and ignore listeners (Blazor usually destroys the node anyway)
            this.instances.delete(id);
        }
    }
};