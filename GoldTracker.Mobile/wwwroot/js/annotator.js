
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
                    const side = Math.max(Math.abs(dx), Math.abs(dy));
                    const nx = this.startX + (dx >= 0 ? side : -side);
                    const ny = this.startY + (dy >= 0 ? side : -side);

                    // Only notify .NET, don't add locally to avoid duplication
                    if (side > 5) {
                        if (this.dotNetHelper) {
                            const startX = Math.min(this.startX, nx) / this.canvas.width;
                            const startY = Math.min(this.startY, ny) / this.canvas.height;
                            const endX = Math.max(this.startX, nx) / this.canvas.width;
                            const endY = Math.max(this.startY, ny) / this.canvas.height;
                            this.dotNetHelper.invokeMethodAsync('AddBox', startX, startY, endX, endY);
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
                    if (this.showBoxes || isSelected) {
                        this.drawBox(box, isSelected);
                    }
                });

                if (this.selectedBoxIndex !== -1) {
                    this.drawMagnifier();
                }
            },

            drawBox: function (box, isSelected) {
                const x = box.startX * this.canvas.width;
                const y = box.startY * this.canvas.height;
                const w = (box.endX - box.startX) * this.canvas.width;
                const h = (box.endY - box.startY) * this.canvas.height;

                this.ctx.save();
                this.ctx.strokeStyle = box.color || '#FF0000';
                this.ctx.lineWidth = isSelected ? 3 : 2;

                // Draw circle (ellipse)
                this.ctx.beginPath();
                this.ctx.ellipse(x + w / 2, y + h / 2, Math.abs(w / 2), Math.abs(h / 2), 0, 0, 2 * Math.PI);
                this.ctx.stroke();

                if (isSelected && !this.readOnly) {
                    this.drawHandles(x, y, w, h);
                }

                // Label
                if (box.label) {
                    this.ctx.fillStyle = box.color || '#FF0000';
                    this.ctx.font = '12px Arial';
                    this.ctx.fillText(box.label, x, y - 5);
                }
                this.ctx.restore();
            },

            drawHandles: function (x, y, w, h) {
                const size = 10;
                this.ctx.fillStyle = '#FFFFFF';
                this.ctx.strokeStyle = '#000000';
                this.ctx.lineWidth = 1;

                const handles = [
                    { x: x, y: y }, { x: x + w, y: y },
                    { x: x, y: y + h }, { x: x + w, y: y + h }
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
                this.ctx.strokeStyle = '#FFF';
                this.ctx.lineWidth = 2;
                this.ctx.beginPath();
                this.ctx.arc(magX + this.magnifierSize / 2, magY + this.magnifierSize / 2, this.magnifierSize / 2, 0, Math.PI * 2);
                this.ctx.stroke();
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
                const tolerance = Math.max(15, r * 0.25); // Min 15px or 25% of radius

                return Math.abs(dist - r) < tolerance;
            },

            getBoxAtPoint: function (pos) {
                let minArea = Infinity;
                let minIndex = -1;

                for (let i = 0; i < this.boxes.length; i++) {
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

                if (Math.abs(pos.x - x) < size && Math.abs(pos.y - y) < size) return 'tl';
                if (Math.abs(pos.x - (x + w)) < size && Math.abs(pos.y - y) < size) return 'tr';
                if (Math.abs(pos.x - x) < size && Math.abs(pos.y - (y + h)) < size) return 'bl';
                if (Math.abs(pos.x - (x + w)) < size && Math.abs(pos.y - (y + h)) < size) return 'br';

                return null;
            },

            moveHandle: function (pos) {
                if (this.selectedBoxIndex === -1 || this.readOnly) return;
                const box = this.boxes[this.selectedBoxIndex];

                let nx = pos.x;
                let ny = pos.y;
                let fixX, fixY;
                if (this.activeHandle === 'tl') { fixX = box.endX * this.canvas.width; fixY = box.endY * this.canvas.height; }
                else if (this.activeHandle === 'tr') { fixX = box.startX * this.canvas.width; fixY = box.endY * this.canvas.height; }
                else if (this.activeHandle === 'bl') { fixX = box.endX * this.canvas.width; fixY = box.startY * this.canvas.height; }
                else if (this.activeHandle === 'br') { fixX = box.startX * this.canvas.width; fixY = box.startY * this.canvas.height; }

                if (fixX !== undefined) {
                    const dx = nx - fixX;
                    const dy = ny - fixY;
                    const side = Math.max(Math.abs(dx), Math.abs(dy));
                    nx = fixX + (dx >= 0 ? side : -side);
                    ny = fixY + (dy >= 0 ? side : -side);

                    if (this.activeHandle === 'tl') { box.startX = nx / this.canvas.width; box.startY = ny / this.canvas.height; }
                    else if (this.activeHandle === 'tr') { box.endX = nx / this.canvas.width; box.startY = ny / this.canvas.height; }
                    else if (this.activeHandle === 'bl') { box.startX = nx / this.canvas.width; box.endY = ny / this.canvas.height; }
                    else if (this.activeHandle === 'br') { box.endX = nx / this.canvas.width; box.endY = ny / this.canvas.height; }
                }

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
                const side = Math.max(Math.abs(dx), Math.abs(dy));
                const nx = this.startX + (dx >= 0 ? side : -side);
                const ny = this.startY + (dy >= 0 ? side : -side);

                const x = Math.min(this.startX, nx);
                const y = Math.min(this.startY, ny);

                this.ctx.strokeStyle = '#FF0000';
                this.ctx.setLineDash([5, 5]);
                this.ctx.strokeRect(x, y, side, side);
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
                color: b.color
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
                color: box.color
            });
            inst.selectedBoxIndex = inst.boxes.length - 1;
            inst.activeHandle = inst.readOnly ? null : 'center';
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

    destroy: function (id) {
        const inst = this.instances.get(id);
        if (inst) {
            window.removeEventListener('resize', inst.onResize);
            // Replace canvas to remove listeners
            const newCanvas = inst.canvas.cloneNode(true);
            inst.canvas.replaceWith(newCanvas);
            this.instances.delete(id);
        }
    }
};